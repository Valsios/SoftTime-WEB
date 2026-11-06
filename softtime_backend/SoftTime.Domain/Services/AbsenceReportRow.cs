namespace SoftTime.Domain.Services;

public sealed record AbsenceReportRow(
    string Matricule,
    string? Nom,
    string? Prenom,
    string? Departement,
    DateTime Date,
    string Periode,
    decimal? DureeHeures,
    string Type,
    string? Motif
);
