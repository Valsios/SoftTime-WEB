namespace SoftTime.Infrastructure.Services;

public sealed record WeekComputation(
    decimal Hs, decimal Nuit, decimal Dimanche, decimal Ferie,
    decimal Retard, decimal Absence, bool Validee);
