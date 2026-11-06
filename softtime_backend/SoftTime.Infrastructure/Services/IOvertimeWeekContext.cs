using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;

namespace SoftTime.Infrastructure.Services;

public interface IOvertimeWeekContext
{
    Task<WeeklyContext> ChargerContexteAsync(PeriodRequest request, bool avecExistantes, CancellationToken ct);

    /// <summary>
    /// Paramétrage du salarié : catégorie (heures hebdo, pause), tolérances, priorité des majorations
    /// et civilité (règle du 8 mars). Les valeurs absentes retombent sur les défauts de la synthèse.
    /// </summary>
    OvertimeWeekConfig Configurer(WeeklyContext ctx, CardPaie card);

    /// <summary>Journées du calcul : paires, planning (reconstruit s'il manque), férié, congé, retard saisi.</summary>
    List<OvertimeDayInput> ConstruireJours(WeeklyContext ctx, CardPaie card, DateTime from, DateTime to);

    /// <summary>
    /// Bornes des lundis (<c>DATE_DEB</c>) des semaines rattachées à une période EXO/IMPO : une semaine
    /// appartient à la période qui contient son dimanche, ce qui l'affecte à une seule période.
    /// </summary>
    (DateTime DebutMin, DateTime DebutMax) BornesSemainesDeLaPeriode(DateTime from, DateTime to);

    void EnsureMaxOneMonth(PeriodRequest request);

    /// <summary>
    /// Cartes du périmètre. Le filtre pointeuse est tolérant (lignes héritées sans base renseignée)
    /// et identique à celui du pré-traitement des pointages.
    /// </summary>
    Task<List<CardPaie>> TargetCardsAsync(PeriodRequest request, CancellationToken ct);
}
