using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EntreQuiz.AdoMcp.Services;

/// <summary>
/// Wrapper fino sobre a REST API de Work Item Tracking do Azure DevOps.
/// Autentica por PAT (Basic auth) e cria work items usando o content-type
/// especial "application/json-patch+json". A hierarquia Épico > História > Task
/// é montada anexando uma relation "System.LinkTypes.Hierarchy-Reverse"
/// que aponta para o work item pai.
/// </summary>
public sealed class AzureDevOpsClient
{
    private const string ApiVersion = "7.1";

    private readonly HttpClient _http;
    private readonly AdoOptions _opts;

    public AzureDevOpsClient(HttpClient http, AdoOptions opts)
    {
        _http = http;
        _opts = opts;

        _http.BaseAddress = new Uri($"https://dev.azure.com/{_opts.Organization}/");

        // PAT vai como Basic auth: usuário vazio, senha = PAT.
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_opts.Pat}"));
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    /// <param name="workItemType">"Epic", "User Story" ou "Task".</param>
    /// <param name="parentId">Id do work item pai (null = item de topo, sem pai).</param>
    public async Task<WorkItemResult> CreateWorkItemAsync(
        string workItemType,
        string title,
        string? description,
        int? parentId,
        CancellationToken ct = default)
    {
        // Documento JSON Patch com as operações de criação.
        var ops = new List<object>
        {
            new { op = "add", path = "/fields/System.Title", value = title }
        };

        if (!string.IsNullOrWhiteSpace(description))
        {
            // O campo System.Description renderiza HTML no board.
            ops.Add(new { op = "add", path = "/fields/System.Description", value = description });
        }

        if (parentId is int pid)
        {
            ops.Add(new
            {
                op = "add",
                path = "/relations/-",
                value = new
                {
                    rel = "System.LinkTypes.Hierarchy-Reverse", // aponta para o PAI
                    url = $"https://dev.azure.com/{_opts.Organization}/_apis/wit/workItems/{pid}"
                }
            });
        }

        var json = JsonSerializer.Serialize(ops);
        using var content = new StringContent(json, Encoding.UTF8, "application/json-patch+json");

        // O tipo vai na URL precedido de "$" literal, com o nome URL-encoded.
        // Ex.: "User Story" => "$User%20Story"
        var typeSegment = "$" + Uri.EscapeDataString(workItemType);
        var requestUri = $"{Uri.EscapeDataString(_opts.Project)}/_apis/wit/workitems/{typeSegment}?api-version={ApiVersion}";

        using var resp = await _http.PostAsync(requestUri, content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Azure DevOps retornou {(int)resp.StatusCode} ao criar '{workItemType}': {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var id = root.GetProperty("id").GetInt32();

        // Prefere a URL "html" (aquela que abre no navegador); cai pra "url" da API se faltar.
        string? url = null;
        if (root.TryGetProperty("_links", out var links) &&
            links.TryGetProperty("html", out var html) &&
            html.TryGetProperty("href", out var href))
        {
            url = href.GetString();
        }
        url ??= root.TryGetProperty("url", out var apiUrl) ? apiUrl.GetString() : null;

        return new WorkItemResult(id, title, workItemType, url ?? string.Empty);
    }
}
