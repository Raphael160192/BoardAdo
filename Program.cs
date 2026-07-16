using System.Security.Cryptography;
using System.Text;
using EntreQuiz.AdoMcp.Services;

var builder = WebApplication.CreateBuilder(args);

// O Render injeta a porta em runtime via env var PORT. Precisamos escutar em
// 0.0.0.0 (não localhost) para o serviço ficar acessível.
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// --- Configuração: tudo via env vars no Render ---
//   ADO_ORG      -> nome da organização no Azure DevOps (ex.: "minhaorg")
//   ADO_PROJECT  -> nome do project (ex.: "Entre 4 Paredes")
//   ADO_PAT      -> Personal Access Token com escopo Work Items (Read & Write)
//   MCP_API_KEY  -> segredo que o Claude precisa enviar (header Authorization ou ?key=)
var config = builder.Configuration;
string Require(string key) =>
    config[key] ?? throw new InvalidOperationException($"Variável de ambiente '{key}' não configurada.");

var adoOptions = new AdoOptions(
    Organization: Require("ADO_ORG"),
    Project: Require("ADO_PROJECT"),
    Pat: Require("ADO_PAT"));
var mcpApiKey = Require("MCP_API_KEY");

builder.Services.AddSingleton(adoOptions);
builder.Services.AddHttpClient<AzureDevOpsClient>();

builder.Services
    .AddMcpServer(o => o.ServerInfo = new() { Name = "entre-quiz-ado", Version = "1.0.0" })
    // Stateless: recomendado para servers hospedados; sem afinidade de sessão,
    // ideal para o Render free (sobrevive a spin down/restart sem quebrar a conexão).
    .WithHttpTransport(o => o.Stateless = true)
    .WithToolsFromAssembly();

var app = builder.Build();

// --- Gate de segurança por chave compartilhada ---
// O endpoint é público na internet. Exigimos a chave em todas as rotas, exceto
// o /health (que o Render usa para checar liveness). Aceitamos a chave de duas
// formas: header "Authorization: Bearer <chave>" (uso via curl/MCP Inspector)
// ou query string "?key=<chave>" (necessário porque o custom connector do
// claude.ai hoje só permite configurar Nome + URL, sem campo de headers).
var expectedAuthHeader = Encoding.UTF8.GetBytes($"Bearer {mcpApiKey}");
var expectedKey = Encoding.UTF8.GetBytes(mcpApiKey);
app.Use(async (context, next) =>
{
    if (context.Request.Path == "/health")
    {
        await next();
        return;
    }

    var providedHeader = Encoding.UTF8.GetBytes(context.Request.Headers.Authorization.ToString());
    var providedKey = Encoding.UTF8.GetBytes(context.Request.Query["key"].ToString());

    var headerMatches = providedHeader.Length == expectedAuthHeader.Length &&
        CryptographicOperations.FixedTimeEquals(providedHeader, expectedAuthHeader);
    var keyMatches = providedKey.Length == expectedKey.Length &&
        CryptographicOperations.FixedTimeEquals(providedKey, expectedKey);

    if (!headerMatches && !keyMatches)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsync("Unauthorized");
        return;
    }

    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "healthy" }));

// Mapeia o endpoint MCP (Streamable HTTP) na raiz "/".
// A URL que você informa ao Claude no custom connector é a raiz do serviço.
app.MapMcp();

app.Run();
