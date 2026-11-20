namespace SoftTime.Application.DTOs;

public record OvertimeSageCodes(string Exo130, string Exo150, string I130, string I150, string Ferie, string Dim, string Nuit)
{
    public OvertimeSageCodes() : this("HS01", "HS02", "HS03", "HS04", "HS05", "HS06", "HS07") { }

    /// <summary>Code constante d'une catégorie d'heures supplémentaires (Exo130 … Nuit).</summary>
    public string CodePour(string categorie) => categorie switch
    {
        "Exo130" => Exo130,
        "Exo150" => Exo150,
        "I130" => I130,
        "I150" => I150,
        "Ferie" => Ferie,
        "Dimanche" => Dim,
        "Nuit" => Nuit,
        _ => throw new DomainException($"Catégorie d'heures supplémentaires inconnue : '{categorie}'.")
    };
}
