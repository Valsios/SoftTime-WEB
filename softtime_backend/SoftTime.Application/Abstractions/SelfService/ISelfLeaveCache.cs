namespace SoftTime.Application.Abstractions.SelfService;

public interface ISelfLeaveCache
{
    /// <summary>
    /// Congés SAGE du salarié <paramref name="me"/>. <paramref name="chargerTenant"/> lit tout le tenant (clé canonique
    /// de matricule → congés) ; elle est appelée au plus une fois à la fois par base et son résultat n'est jamais rendu
    /// en entier : seule la part du demandeur sort du cache.
    /// </summary>
    Task<List<LeaveDay>> ObtenirAsync(
        SelfContext me, Func<Task<Dictionary<string, List<LeaveDay>>>> chargerTenant, CancellationToken ct = default);
}
