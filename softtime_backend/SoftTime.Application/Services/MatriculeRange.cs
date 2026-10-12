namespace SoftTime.Application.Services;

/// <summary>Filtre de plage de matricules partagé par les opérations de masse (purges notamment).</summary>
internal static class MatriculeRange
{
    public static bool Contains(string? matricule, DTOs.PeriodRequest filter)
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
