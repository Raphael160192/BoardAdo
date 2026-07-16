using System.ComponentModel;
using EntreQuiz.AdoMcp.Services;
using ModelContextProtocol.Server;

namespace EntreQuiz.AdoMcp.Tools;

/// <summary>
/// Ferramentas que o Claude enxerga. As descrições ([Description]) são o que
/// o modelo lê para decidir quando e como chamar cada uma — vale caprichar nelas.
/// Cada tool devolve o Id do item criado, o que permite ao Claude encadear a
/// hierarquia sem intervenção: épico -> histórias filhas -> tasks filhas.
/// </summary>
[McpServerToolType]
public sealed class WorkItemTools(AzureDevOpsClient ado)
{
    [McpServerTool(Name = "create_epic")]
    [Description("Cria um Épico no Azure DevOps. Use para uma iniciativa ou ideia grande que ainda será refinada em histórias. Retorna o id do épico, que deve ser usado como parentId das histórias.")]
    public Task<WorkItemResult> CreateEpic(
        [Description("Título curto e claro do épico.")] string title,
        [Description("Descrição do épico (HTML ou texto). Opcional.")] string? description = null,
        CancellationToken ct = default)
        => ado.CreateWorkItemAsync("Epic", title, description, parentId: null, ct);

    [McpServerTool(Name = "create_user_story")]
    [Description("Cria uma User Story vinculada a um épico pai. A história deve representar um incremento de valor entregável. Retorna o id da história, que deve ser usado como parentId das tasks.")]
    public Task<WorkItemResult> CreateUserStory(
        [Description("Título da história (idealmente no formato 'Como <persona>, quero <objetivo>').")] string title,
        [Description("Id do épico pai ao qual esta história será vinculada como filha.")] int parentId,
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
}
