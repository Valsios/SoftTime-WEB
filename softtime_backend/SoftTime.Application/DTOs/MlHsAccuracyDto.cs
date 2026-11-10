namespace SoftTime.Application.DTOs;

/// <summary>Prévu / réel d'une semaine terminée (salariés présents des deux côtés).</summary>
public sealed record MlHsAccuracyDto(
    DateTime SemaineDebut,
    int Salaries,
    decimal HeuresPrevues,
    decimal HeuresReelles,
    decimal EcartMoyen,
    double Couverture);
