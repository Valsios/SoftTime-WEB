using SoftTime.Application.DTOs;

namespace SoftTime.Infrastructure.Services;

internal static class HistoryPeriods
{

    // ---------------------------------------------------------------- Interne

    internal static bool DansPlage(string? matricule, PeriodRequest filter)
    {
        if (string.IsNullOrEmpty(filter.MatriculeFrom) && string.IsNullOrEmpty(filter.MatriculeTo))
            return true;
        if (string.IsNullOrEmpty(matricule))
            return false;
        if (!string.IsNullOrEmpty(filter.MatriculeFrom)
            && string.Compare(matricule, filter.MatriculeFrom, StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        if (!string.IsNullOrEmpty(filter.MatriculeTo)
            && string.Compare(matricule, filter.MatriculeTo, StringComparison.OrdinalIgnoreCase) > 0)
            return false;
        return true;
    }
}
