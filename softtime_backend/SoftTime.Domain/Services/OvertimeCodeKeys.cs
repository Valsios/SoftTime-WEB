namespace SoftTime.Domain.Services;

public static class OvertimeCodeKeys
{
    public const string Exo130 = "Exo130";
    public const string Exo150 = "Exo150";
    public const string I130 = "I130";
    public const string I150 = "I150";
    public const string Ferie = "Ferie";
    public const string Dim = "Dim";
    public const string Nuit = "Nuit";

    public static readonly IReadOnlyList<(string Key, string Intitule, string DefaultCode)> Defaults =
    [
        (Exo130, "EXO 130%", "HS01"),
        (Exo150, "EXO 150%", "HS02"),
        (I130, "IMPO 130%", "HS03"),
        (I150, "IMPO 150%", "HS04"),
        (Ferie, "Férié", "HS05"),
        (Dim, "Dimanche", "HS06"),
        (Nuit, "Nuit", "HS07"),
    ];
}
