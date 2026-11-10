namespace SoftTime.Infrastructure.Services.DataSources;

/// <summary>Tables attendues d'une base standard et messages du test de connexion.</summary>
internal sealed record StandardStructure(IReadOnlyList<string> Tables, string Reconnue, string Manquante);
