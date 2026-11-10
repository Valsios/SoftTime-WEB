namespace SoftTime.Application.Services;

/// <summary>Paramétrage de l'atterrissage de semaine (section <c>Landing</c> de appsettings.json).</summary>
public sealed class LandingOptions
{
    public const string Section = "Landing";

    /// <summary>Semaines pointées utilisées pour mesurer l'écart habituel d'un salarié à son planning.</summary>
    public int DriftWeeks { get; set; } = 4;

    /// <summary>Semaines affichées sur l'horizon, semaine en cours comprise.</summary>
    public int HorizonWeeks { get; set; } = 4;
}
