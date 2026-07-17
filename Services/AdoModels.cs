namespace EntreQuiz.AdoMcp.Services;

/// <summary>Configuração do destino no Azure DevOps. Preenchida via env vars.</summary>
public sealed record AdoOptions(string Organization, string Project, string Pat);

/// <summary>
/// Resultado retornado ao Claude após criar um work item.
/// O <see cref="Id"/> é o que permite ao Claude encadear a hierarquia
/// (criar histórias filhas do épico, tasks filhas da história, etc.).
/// </summary>
public sealed record WorkItemResult(int Id, string Title, string Type, string Url);

/// <summary>
/// Versão enxuta de um work item, usada nos resultados de listagem.
/// Só os campos que cabem numa visão de lista — para o resto, use get_work_item.
/// </summary>
public sealed record WorkItemSummary(int Id, string Title, string Type, string State, string Url);

/// <summary>
/// Work item completo, incluindo os vínculos de hierarquia. O <see cref="ParentId"/>
/// e os <see cref="ChildIds"/> permitem ao Claude navegar a árvore do backlog.
/// </summary>
public sealed record WorkItemDetail(
    int Id,
    string Title,
    string Type,
    string State,
    string? Description,
    string? AssignedTo,
    int? ParentId,
    IReadOnlyList<int> ChildIds,
    string Url);
