namespace SoftTime.Domain.Models.Ml;

public static class MlModelTypes
{
    public const string Absence = "Absence";
    public const string HsProbabilite = "HsProbabilite";
    public const string HsMontant = "HsMontant";

    public static string ToStorage(MlModelType type) => type switch
    {
        MlModelType.Absence => Absence,
        MlModelType.HsProbabilite => HsProbabilite,
        MlModelType.HsMontant => HsMontant,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public static MlModelType Parse(string value) => value switch
    {
        Absence => MlModelType.Absence,
        HsProbabilite => MlModelType.HsProbabilite,
        HsMontant => MlModelType.HsMontant,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Type de modèle inconnu.")
    };
}
