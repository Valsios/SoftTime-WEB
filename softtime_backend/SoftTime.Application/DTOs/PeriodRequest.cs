namespace SoftTime.Application.DTOs;

/// <param name="OnlyWithHours">
/// Validation semaine seulement : masque les salariés sans heure corrigée ni jour planifié sur la
/// période. Ignoré par les autres écrans.
/// </param>
public record PeriodRequest(string? MatriculeFrom, string? MatriculeTo, DateTime From, DateTime To, string? Departement = null, string? Search = null, bool OnlyWithHours = false)
{
    public PeriodRequest() : this(default, default, default, default, default, default, default) { }
}
