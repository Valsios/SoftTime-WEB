namespace SoftTime.Infrastructure.Services.DataSources;

/// <summary>Règles communes à l'enregistrement d'une base SAGE ou pointeuse.</summary>
internal static class DatabaseTypeBase
{
    public const string DoublonMessage = "Cette base (serveur + nom) est déjà enregistrée.";

    /// <summary>Type « STANDARD » ou « AUTRE » ; une base « AUTRE » exige au moins un mapping de source.</summary>
    public static void Validate(string? typeBase, int mappings)
    {
        if (typeBase != "STANDARD" && typeBase != "AUTRE")
            throw new DomainException("TypeBase doit valoir 'STANDARD' ou 'AUTRE'.");
        if (typeBase == "AUTRE" && mappings == 0)
            throw new DomainException("Une base 'Autre' nécessite au minimum un mapping de source.");
    }
}
