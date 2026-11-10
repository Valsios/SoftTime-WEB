namespace SoftTime.Tests;

/// <summary>
/// Test ignoré si la variable d'environnement SOFTTIME_MIGTEST_CONN n'existe pas. Sa valeur
/// (authentification Windows, jamais écrite dans le dépôt) doit viser une base JETABLE dont le
/// nom commence par SOFTTIME_MIGTEST_ ; toute autre base est refusée.
/// </summary>
public sealed class MigrationIntegrationFactAttribute : FactAttribute
{
    public const string Variable = "SOFTTIME_MIGTEST_CONN";

    public MigrationIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Variable d'environnement {Variable} absente : test d'intégration des migrations ignoré.";
    }
}
