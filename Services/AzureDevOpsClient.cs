using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace EntreQuiz.AdoMcp.Services;

/// <summary>
/// Wrapper fino sobre a REST API de Work Item Tracking do Azure DevOps.
/// Autentica por PAT (Basic auth) e cria work items usando o content-type
/// especial "application/json-patch+json". A hierarquia (Epic > Feature >
/// User Story > Task) é montada anexando uma relation
/// "System.LinkTypes.Hierarchy-Reverse" que aponta para o work item pai.
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

    /// <param name="workItemType">"Epic", "Feature", "User Story", "Task", "Bug" ou "Issue".</param>
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

    /// <summary>Busca um work item pelo id, já resolvendo pai e filhos.</summary>
    public async Task<WorkItemDetail> GetWorkItemAsync(int id, CancellationToken ct = default)
    {
        // $expand=relations traz os links de hierarquia junto com os campos.
        var requestUri = $"{Uri.EscapeDataString(_opts.Project)}/_apis/wit/workitems/{id}" +
                         $"?$expand=relations&api-version={ApiVersion}";

        using var resp = await _http.GetAsync(requestUri, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Azure DevOps retornou {(int)resp.StatusCode} ao consultar o work item {id}: {body}");
        }

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var fields = root.GetProperty("fields");

        int? parentId = null;
        var childIds = new List<int>();

        if (root.TryGetProperty("relations", out var relations))
        {
            foreach (var relation in relations.EnumerateArray())
            {
                var rel = relation.TryGetProperty("rel", out var r) ? r.GetString() : null;
                var relUrl = relation.TryGetProperty("url", out var u) ? u.GetString() : null;

                if (IdFromUrl(relUrl) is not int relatedId) continue;

                // Reverse aponta para o pai; Forward aponta para os filhos.
                if (rel == "System.LinkTypes.Hierarchy-Reverse") parentId = relatedId;
                else if (rel == "System.LinkTypes.Hierarchy-Forward") childIds.Add(relatedId);
            }
        }

        return new WorkItemDetail(
            Id: id,
            Title: Field(fields, "System.Title") ?? string.Empty,
            Type: Field(fields, "System.WorkItemType") ?? string.Empty,
            State: Field(fields, "System.State") ?? string.Empty,
            Description: Field(fields, "System.Description"),
            AssignedTo: DisplayName(fields, "System.AssignedTo"),
            ParentId: parentId,
            ChildIds: childIds,
            Url: HtmlUrl(id));
    }

    /// <summary>
    /// Lista work items do projeto aplicando filtros opcionais. Monta o WIQL
    /// internamente — o Claude passa filtros estruturados, nunca query crua.
    /// </summary>
    public async Task<IReadOnlyList<WorkItemSummary>> QueryWorkItemsAsync(
        string? type = null,
        string? state = null,
        string? titleContains = null,
        int? parentId = null,
        int top = 50,
        CancellationToken ct = default)
    {
        // O batch de leitura de campos aceita no máximo 200 ids por chamada.
        top = Math.Clamp(top, 1, 200);

        var conditions = new List<string> { $"[System.TeamProject] = '{Escape(_opts.Project)}'" };

        if (!string.IsNullOrWhiteSpace(type))
            conditions.Add($"[System.WorkItemType] = '{Escape(type)}'");
        if (!string.IsNullOrWhiteSpace(state))
            conditions.Add($"[System.State] = '{Escape(state)}'");
        if (!string.IsNullOrWhiteSpace(titleContains))
            conditions.Add($"[System.Title] CONTAINS '{Escape(titleContains)}'");
        if (parentId is int pid)
            conditions.Add($"[System.Parent] = {pid}");

        var wiql = $"SELECT [System.Id] FROM WorkItems WHERE {string.Join(" AND ", conditions)} " +
                   "ORDER BY [System.ChangedDate] DESC";

        var queryUri = $"{Uri.EscapeDataString(_opts.Project)}/_apis/wit/wiql?$top={top}&api-version={ApiVersion}";
        using var queryContent = new StringContent(
            JsonSerializer.Serialize(new { query = wiql }), Encoding.UTF8, "application/json");

        using var queryResp = await _http.PostAsync(queryUri, queryContent, ct);
        var queryBody = await queryResp.Content.ReadAsStringAsync(ct);

        if (!queryResp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Azure DevOps retornou {(int)queryResp.StatusCode} ao executar a consulta: {queryBody}");
        }

        using var queryDoc = JsonDocument.Parse(queryBody);
        var ids = queryDoc.RootElement.GetProperty("workItems")
            .EnumerateArray()
            .Select(w => w.GetProperty("id").GetInt32())
            .ToList();

        if (ids.Count == 0) return Array.Empty<WorkItemSummary>();

        // O WIQL devolve só ids; os campos vêm num segundo request em batch.
        const string fieldList = "System.Title,System.WorkItemType,System.State";
        var batchUri = $"{Uri.EscapeDataString(_opts.Project)}/_apis/wit/workitems" +
                       $"?ids={string.Join(',', ids)}&fields={fieldList}&api-version={ApiVersion}";

        using var batchResp = await _http.GetAsync(batchUri, ct);
        var batchBody = await batchResp.Content.ReadAsStringAsync(ct);

        if (!batchResp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Azure DevOps retornou {(int)batchResp.StatusCode} ao buscar os campos: {batchBody}");
        }

        using var batchDoc = JsonDocument.Parse(batchBody);
        var byId = new Dictionary<int, WorkItemSummary>();

        foreach (var item in batchDoc.RootElement.GetProperty("value").EnumerateArray())
        {
            var itemId = item.GetProperty("id").GetInt32();
            var fields = item.GetProperty("fields");

            byId[itemId] = new WorkItemSummary(
                itemId,
                Field(fields, "System.Title") ?? string.Empty,
                Field(fields, "System.WorkItemType") ?? string.Empty,
                Field(fields, "System.State") ?? string.Empty,
                HtmlUrl(itemId));
        }

        // O batch não preserva a ordem do WIQL; reordenamos pelo ORDER BY original.
        return ids.Where(byId.ContainsKey).Select(id => byId[id]).ToList();
    }

    private static string? Field(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var value) ? value.GetString() : null;

    /// <summary>System.AssignedTo vem como objeto (identidade), não como string.</summary>
    private static string? DisplayName(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var identity) &&
        identity.TryGetProperty("displayName", out var display)
            ? display.GetString()
            : null;

    /// <summary>Extrai o id do último segmento de uma URL de work item.</summary>
    private static int? IdFromUrl(string? url)
    {
        if (string.IsNullOrEmpty(url)) return null;
        var lastSlash = url.LastIndexOf('/');
        return int.TryParse(url.AsSpan(lastSlash + 1), out var id) ? id : null;
    }

    /// <summary>URL que abre o work item no navegador (a "url" da API não é navegável).</summary>
    private string HtmlUrl(int id) =>
        $"https://dev.azure.com/{_opts.Organization}/{Uri.EscapeDataString(_opts.Project)}/_workitems/edit/{id}";

    /// <summary>Aspas simples são escapadas dobrando, como em SQL.</summary>
    private static string Escape(string value) => value.Replace("'", "''");
}
