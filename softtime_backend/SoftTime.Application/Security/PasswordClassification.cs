namespace SoftTime.Application.Security;

/// <summary>
/// Classification fine des valeurs stockées dans la colonne PASSWORD.
/// Permet de distinguer avec précision les hachages supportés, les formats inconnus,
/// les valeurs corrompues et les mots de passe historiques en clair.
/// </summary>
public enum PasswordClassification
{
    NullOrEmpty = 0,
    IdentityV3 = 1,
    IdentityV2 = 2,
    LegacyPlainText = 3,
    UnrecognizedHash = 4,
    AmbiguousOrMalformed = 5
}
