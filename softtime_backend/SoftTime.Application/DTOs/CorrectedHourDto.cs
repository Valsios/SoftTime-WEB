namespace SoftTime.Application.DTOs;

public record CorrectedHourDto(decimal Id, string? Matricule, DateTime? DateIn, DateTime? DateOut, TimeSpan? HEntree, TimeSpan? HPs, TimeSpan? HPe, TimeSpan? HSortie, bool? AbsAm, bool? AbsPm, string? IntituleAbsence, TimeSpan? Retard, bool? Ferie, bool? FerieAm, bool? FeriePm, bool? ValiderCorrection, bool? ValiderHs, decimal? Hs, decimal? MNuit, decimal? MDimanche, decimal? MFeries)
{
    public CorrectedHourDto() : this(default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default, default) { }
}
