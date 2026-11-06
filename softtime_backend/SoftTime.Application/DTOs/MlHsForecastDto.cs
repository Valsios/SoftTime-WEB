namespace SoftTime.Application.DTOs;

public sealed record MlHsForecastDto(
    string Matricule,
    string? Nom,
    string? Prenom,
    string? Departement,
    DateTime SemaineDebut,
    decimal HeuresPrevues,
    decimal BorneBasse,
    decimal BorneHaute,
    double ProbabiliteDepassement,
    byte Niveau);
