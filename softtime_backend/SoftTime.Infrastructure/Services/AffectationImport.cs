using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

/// <summary>
/// Un import Excel d'affectations salarié → catégorie : une ligne = (catégorie, matricule). Tient le
/// bilan des lignes vides, catégories et matricules introuvables. Une instance par import.
/// </summary>
internal sealed class AffectationImport
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private List<Categorie> _categories = new();
    private List<CardPaie> _cards = new();
    private List<CategorieSalarie> _existing = new();
    private readonly List<string> _categoriesIntrouvables = new();
    private readonly List<string> _matriculesIntrouvables = new();
    private int _imported;
    private int _lignesVides;
    private int _categoriesManquantes;
    private int _matriculesManquants;

    public AffectationImport(IUnitOfWork uow, ITenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
    }

    private int Skipped => _lignesVides + _categoriesManquantes + _matriculesManquants;

    public async Task ChargerAsync(CancellationToken ct)
    {
        _categories = await _uow.Repository<Categorie>().ListAsync(c => c.BddSage == _tenant.SageDb, ct);
        _cards = await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == _tenant.SageDb, ct);
        _existing = await _uow.Repository<CategorieSalarie>().ListAsync(a => a.BddSage == _tenant.SageDb, ct);
    }

    public async Task ImporterAsync(Dictionary<string, string> row, CancellationToken ct)
    {
        var catName = ExcelCells.Get(row, "Categorie", "CATEGORIE", "Intitule", "Intitulé")
                      ?? row.Values.FirstOrDefault();
        var mat = ExcelCells.Get(row, "Matricule", "MATRICULE", "Matricule SAGE")
                  ?? (row.Count > 1 ? row.Values.Skip(1).FirstOrDefault() : null);
        if (string.IsNullOrWhiteSpace(catName) || string.IsNullOrWhiteSpace(mat))
        {
            _lignesVides++;
            return;
        }
        var cat = _categories.FirstOrDefault(c => string.Equals(c.Intitule?.Trim(), catName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (cat == null)
        {
            _categoriesManquantes++;
            AjouterValeurDistincte(_categoriesIntrouvables, catName);
            return;
        }
        var card = _cards.FirstOrDefault(c => string.Equals(c.SageMatricule?.Trim(), mat.Trim(), StringComparison.OrdinalIgnoreCase));
        if (card == null)
        {
            _matriculesManquants++;
            AjouterValeurDistincte(_matriculesIntrouvables, mat);
            return;
        }
        await AffecterAsync(card, cat, ct);
        _imported++;
    }

    public ImportResultDto Resultat()
    {
        var raisons = new List<string>();
        if (_lignesVides > 0)
            raisons.Add($"{_lignesVides} ligne(s) vide(s)");
        if (_categoriesManquantes > 0)
            raisons.Add($"{_categoriesManquantes} catégorie(s) introuvable(s) : {ApercuValeurs(_categoriesIntrouvables)}");
        if (_matriculesManquants > 0)
            raisons.Add($"{_matriculesManquants} matricule(s) introuvable(s) : {ApercuValeurs(_matriculesIntrouvables)}");
        var message = $"{_imported} affectation(s) importée(s), {Skipped} ligne(s) ignorée(s)";
        if (raisons.Count > 0)
            message += $" ({string.Join(" ; ", raisons)})";
        return new ImportResultDto(_imported, Skipped, message + ".");
    }

    private async Task AffecterAsync(CardPaie card, Categorie cat, CancellationToken ct)
    {
        var repo = _uow.Repository<CategorieSalarie>();
        var found = _existing.FirstOrDefault(a => a.MatriculeSage == card.Id);
        if (found != null)
        {
            found.IdCateg = cat.IdCategorie;
            repo.Update(found);
            return;
        }
        found = new CategorieSalarie
        {
            MatriculeSage = card.Id,
            IdCateg = cat.IdCategorie,
            BddSage = _tenant.SageDb,
            BddPointeuse = _tenant.PointeuseDb
        };
        await repo.AddAsync(found, ct);
        _existing.Add(found);
    }

    /// <summary>Ajoute une valeur à afficher dans le bilan, sans doublon (comparaison insensible à la casse).</summary>
    private static void AjouterValeurDistincte(List<string> valeurs, string valeur)
    {
        var propre = valeur.Trim();
        if (!valeurs.Contains(propre, StringComparer.OrdinalIgnoreCase))
            valeurs.Add(propre);
    }

    /// <summary>Liste les premières valeurs fautives pour le message d'import.</summary>
    private static string ApercuValeurs(IReadOnlyList<string> valeurs)
        => string.Join(", ", valeurs.Take(5)) + (valeurs.Count > 5 ? ", …" : string.Empty);
}
