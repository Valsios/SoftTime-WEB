using System.Text.Json.Serialization;

namespace SoftTime.Application.DTOs;

public record HsExoDto(int Oid, string? Matricule, int? NumSal, decimal? Exo130, decimal? Exo150, decimal? I130, decimal? I150, decimal? Ferie, decimal? Nuit, decimal? Dim, decimal? Retard, decimal? Absence)
{
    public HsExoDto() : this(default, default, default, default, default, default, default, default, default, default, default, default) { }

    /// <summary>
    /// Ligne synthétique d'un salarié du périmètre de l'envoi sans ligne de valeurs : toutes ses valeurs sont nulles et
    /// l'envoi écrit 0 pour chaque variable cochée. Jamais lue en base ni exposée par l'API.
    /// </summary>
    [JsonIgnore]
    public bool SansValeur { get; init; }
}
