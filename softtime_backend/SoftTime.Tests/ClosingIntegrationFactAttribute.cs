namespace SoftTime.Tests;

/// <summary>
/// Test ignoré si la variable d'environnement SOFTTIME_CLOTURE_CONN n'existe pas. Sa valeur
/// (authentification Windows, jamais écrite dans le dépôt) doit viser la copie jetable
/// <c>SOFTTIME_DEMO_CLOTURE</c> ; toute autre base est refusée. Lecture seule.
/// </summary>
public sealed class ClosingIntegrationFactAttribute : FactAttribute
{
    public const string Variable = "SOFTTIME_CLOTURE_CONN";

    public ClosingIntegrationFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
            Skip = $"Variable d'environnement {Variable} absente : test d'intégration de la clôture ignoré.";
    }
}
