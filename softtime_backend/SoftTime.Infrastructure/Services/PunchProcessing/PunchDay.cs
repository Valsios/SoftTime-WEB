namespace SoftTime.Infrastructure.Services.PunchProcessing;

/// <summary>Jour traité pour un salarié : matricule tel que saisi, clé normalisée et date.</summary>
internal readonly record struct PunchDay(string Matricule, string Cle, DateTime Jour);
