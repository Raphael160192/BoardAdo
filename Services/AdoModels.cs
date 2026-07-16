namespace EntreQuiz.AdoMcp.Services;

/// <summary>Configuração do destino no Azure DevOps. Preenchida via env vars.</summary>
public sealed record AdoOptions(string Organization, string Project, string Pat);

/// <summary>
/// Resultado retornado ao Claude após criar um work item.
/// O <see cref="Id"/> é o que permite ao Claude encadear a hierarquia
/// (criar histórias filhas do épico, tasks filhas da história, etc.).
/// </summary>
public sealed record WorkItemResult(int Id, string Title, string Type, string Url);
