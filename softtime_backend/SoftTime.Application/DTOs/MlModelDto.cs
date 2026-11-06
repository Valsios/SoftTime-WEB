namespace SoftTime.Application.DTOs;

/// <summary>Modèle exposé au frontend (sans le binaire ML.NET).</summary>
public sealed record MlModelDto(
    int Id,
    string TypeModele,
    string Version,
    string Algorithme,
    string Statut,
    bool EstActif,
    DateTime EntraineLe,
    DateTime PeriodeDebut,
    DateTime PeriodeFin,
    int NbLignes,
    int? NbPositifs,
    string MetriquesJson,
    string? Commentaire);
