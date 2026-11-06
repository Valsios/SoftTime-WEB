namespace SoftTime.Application.DTOs;

public record HsExoDto(int Oid, string? Matricule, int? NumSal, decimal? Exo130, decimal? Exo150, decimal? I130, decimal? I150, decimal? Ferie, decimal? Nuit, decimal? Dim, decimal? Retard, decimal? Absence)
{
    public HsExoDto() : this(default, default, default, default, default, default, default, default, default, default, default, default) { }
}
