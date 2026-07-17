using System.ComponentModel;
using EntreQuiz.AdoMcp.Services;
using ModelContextProtocol.Server;

namespace EntreQuiz.AdoMcp.Tools;

/// <summary>
/// Ferramentas que o Claude enxerga. As descrições ([Description]) são o que
/// o modelo lê para decidir quando e como chamar cada uma — vale caprichar nelas.
/// Cada tool de criação devolve o Id do item criado, o que permite ao Claude
/// encadear a hierarquia sem intervenção: épico -> feature -> histórias -> tasks.
/// A hierarquia do process Agile é Epic > Feature > User Story > Task.
/// </summary>
[McpServerToolType]
public sealed class WorkItemTools(AzureDevOpsClient ado)
{
    [McpServerTool(Name = "create_epic")]
    [Description("Cria um Épico no Azure DevOps. Use para uma iniciativa ou ideia grande que ainda será refinada. Retorna o id do épico, que deve ser usado como parentId das features (ou das histórias, se não houver feature).")]
    public Task<WorkItemResult> CreateEpic(
        [Description("Título curto e claro do épico.")] string title,
        [Description("Descrição do épico (HTML ou texto). Opcional.")] string? description = null,
        CancellationToken ct = default)
        => ado.CreateWorkItemAsync("Epic", title, description, parentId: null, ct);

    [McpServerTool(Name = "create_feature")]
    [Description("Cria uma Feature vinculada a um épico pai. A feature é um agrupamento de histórias que entrega uma capacidade do produto — o nível entre o épico e as histórias. Retorna o id da feature, que pode ser usado como parentId das histórias.")]
    public Task<WorkItemResult> CreateFeature(
        [Description("Título da feature (capacidade do produto, ex.: 'Ranking de jogadores').")] string title,
        [Description("Id do épico pai. Opcional: omita para criar a feature sem pai.")] int? parentId = null,
        [Description("Descrição da feature (HTML ou texto). Opcional.")] string? description = null,
        CancellationToken ct = default)
        => ado.CreateWorkItemAsync("Feature", title, description, parentId, ct);

    [McpServerTool(Name = "create_user_story")]
    [Description("Cria uma User Story vinculada a um pai (feature, ou épico quando não há feature). A história deve representar um incremento de valor entregável. Retorna o id da história, que deve ser usado como parentId das tasks.")]
    public Task<WorkItemResult> CreateUserStory(
        [Description("Título da história (idealmente no formato 'Como <persona>, quero <objetivo>').")] string title,
        [Description("Id do pai (feature ou épico) ao qual esta história será vinculada como filha.")] int parentId,
        [Description("Descrição e/ou critérios de aceite (HTML ou texto). Opcional.")] string? description = null,
        CancellationToken ct = default)
        => ado.CreateWorkItemAsync("User Story", title, description, parentId, ct);

    [McpServerTool(Name = "create_task")]
    [Description("Cria uma Task vinculada a uma User Story pai. A task é um passo técnico concreto e executável da história. Crie as tasks na ordem de execução.")]
    public Task<WorkItemResult> CreateTask(
        [Description("Título da task (ação concreta, ex.: 'Criar migration da tabela Lote').")] string title,
        [Description("Id da história pai à qual esta task será vinculada como filha.")] int parentId,
        [Description("Detalhe técnico da task (HTML ou texto). Opcional.")] string? description = null,
        CancellationToken ct = default)
        => ado.CreateWorkItemAsync("Task", title, description, parentId, ct);

    [McpServerTool(Name = "create_bug")]
    [Description("Cria um Bug — um defeito em algo que já existe e deveria funcionar. Não use para trabalho novo (isso é User Story ou Task). Pode ficar solto ou pendurado numa história/feature.")]
    public Task<WorkItemResult> CreateBug(
        [Description("Título do bug (o sintoma observado, ex.: 'Placar zera ao voltar de background').")] string title,
        [Description("Id do pai (história ou feature) onde o bug foi encontrado. Opcional: omita para criar o bug sem pai.")] int? parentId = null,
        [Description("Passos para reproduzir, resultado esperado e resultado obtido (HTML ou texto). Opcional.")] string? description = null,
        CancellationToken ct = default)
        => ado.CreateWorkItemAsync("Bug", title, description, parentId, ct);

    [McpServerTool(Name = "create_issue")]
    [Description("Cria um Issue — um impedimento, risco ou pendência que bloqueia o time e precisa de acompanhamento, mas não é entrega de produto nem defeito. No process Agile o Issue vive fora da hierarquia do backlog.")]
    public Task<WorkItemResult> CreateIssue(
        [Description("Título do issue (o impedimento, ex.: 'Aguardando acesso à conta de publicação da loja').")] string title,
        [Description("Id do work item pai relacionado. Opcional: normalmente o issue não tem pai.")] int? parentId = null,
        [Description("Contexto do impedimento e o que destrava (HTML ou texto). Opcional.")] string? description = null,
        CancellationToken ct = default)
        => ado.CreateWorkItemAsync("Issue", title, description, parentId, ct);

    [McpServerTool(Name = "get_work_item")]
    [Description("Consulta um work item pelo id e devolve os detalhes, incluindo estado, responsável, descrição, o id do pai e os ids dos filhos. Use os ids de pai/filhos para navegar a árvore do backlog item a item.")]
    public Task<WorkItemDetail> GetWorkItem(
        [Description("Id do work item (o número que aparece no board).")] int id,
        CancellationToken ct = default)
        => ado.GetWorkItemAsync(id, ct);

    [McpServerTool(Name = "list_work_items")]
    [Description("Lista work items do projeto, dos alterados mais recentemente para os mais antigos. Todos os filtros são opcionais e se combinam; sem nenhum filtro devolve os itens mais recentes do projeto. Devolve uma visão resumida — use get_work_item para os detalhes de um item específico.")]
    public Task<IReadOnlyList<WorkItemSummary>> ListWorkItems(
        [Description("Filtra por tipo: 'Epic', 'Feature', 'User Story', 'Task', 'Bug' ou 'Issue'. Omita para todos os tipos.")] string? type = null,
        [Description("Filtra por estado, ex.: 'New', 'Active', 'Resolved', 'Closed'. Omita para todos os estados.")] string? state = null,
        [Description("Filtra por trecho contido no título (busca parcial, sem diferenciar maiúsculas).")] string? titleContains = null,
        [Description("Filtra pelos filhos diretos deste work item pai. Ex.: passe o id de um épico para ver suas features.")] int? parentId = null,
        [Description("Máximo de itens a devolver (1 a 200). Padrão: 50.")] int top = 50,
        CancellationToken ct = default)
        => ado.QueryWorkItemsAsync(type, state, titleContains, parentId, top, ct);
}
