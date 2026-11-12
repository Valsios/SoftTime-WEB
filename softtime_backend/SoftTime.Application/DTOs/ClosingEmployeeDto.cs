namespace SoftTime.Application.DTOs;

/// <summary>
/// Anomalies d'un salarié sur la période analysée, pour « qui traiter en premier ». <c>Poids</c> est la somme des
/// poids de ses anomalies (sa part dans la perte de score) ; <c>ParType</c> ne liste que les types présents.
/// Aucun nom ni prénom ici : la présentation les ajoute selon <c>Closing:ShowNames</c>.
/// </summary>
public sealed record ClosingEmployeeDto(
    string Matricule,
    string? Departement,
    int NombreAnomalies,
    int Poids,
    IReadOnlyList<ClosingCountDto> ParGravite,
    IReadOnlyList<ClosingCountDto> ParType);
