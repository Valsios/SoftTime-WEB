using SoftTime.Application.DTOs;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.Reports;

internal static class ReportPeriods
{

    // ---------------------------------------------------------------- Prédicats / projections

    internal static void EnsureMondayToSunday(ReportFilter filter)
    {
        if (!OvertimeRules.IsMondayToSundayWeek(filter.From, filter.To))
            throw new DomainException("La semaine doit aller du lundi au dimanche.");
    }
}
