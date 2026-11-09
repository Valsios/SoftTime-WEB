namespace SoftTime.Application.DTOs;

/// <summary>
/// Une étape de la préparation de paie. <c>Etat</c> : Fait, AFaire, Information ou NonDeterminable (jamais deviné).
/// <c>Nombre</c> : volume utile à l'étape (à faire, à valider, concernés), <c>null</c> si sans objet.
/// Aucun matricule ni nom : des comptages seulement.
/// </summary>
public sealed record ClosingChecklistLineDto(
    string Code, string Libelle, string Etat, string Detail, int? Nombre = null);
