namespace SoftTime.Domain.Entities.Sage;

public class SalarieSage
{
    public int SaCompteurNumero { get; set; }

    public string MatriculeSalarie { get; set; } = string.Empty;
    public string? Nom { get; set; }
    public string? Prenom { get; set; }
    public string? NumeroDeBadge { get; set; }
    public byte? SalarieDesactive { get; set; }

    /// <summary>
    /// Civilité SAGE (colonne <c>T_SAL.Civilite</c>, TinyInt) : <c>0</c> = homme, valeur non nulle = femme.
    /// Pilote la règle du 8 mars du calcul des HS (synthèse §4.3 d).
    /// </summary>
    public byte? Civilite { get; set; }
}
