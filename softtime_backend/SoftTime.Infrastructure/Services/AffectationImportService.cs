using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Application.Services;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class AffectationImportService : IAffectationImportService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;

    public AffectationImportService(
        IUnitOfWork uow,
        ITenantConnectionService tenant)
    {
        _uow = uow;
        _tenant = tenant;
    }

    public async Task<ImportResultDto> ImportAffectationsExcelAsync(IReadOnlyList<Dictionary<string, string>> rows, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var cats = await _uow.Repository<Categorie>().ListAsync(c => c.BddSage == _tenant.SageDb, ct);
        var cards = await _uow.Repository<CardPaie>().ListAsync(c => c.SageBdd == _tenant.SageDb, ct);
        var repo = _uow.Repository<CategorieSalarie>();
        var existing = await repo.ListAsync(a => a.BddSage == _tenant.SageDb, ct);
        var imported = 0;
        var skipped = 0;
        var lignesVides = 0;
        var categoriesManquantes = 0;
        var matriculesManquants = 0;
        var categoriesIntrouvables = new List<string>();
        var matriculesIntrouvables = new List<string>();
        foreach (var row in rows)
        {
            var catName = ExcelCells.Get(row, "Categorie", "CATEGORIE", "Intitule", "Intitulé")
                        ?? row.Values.FirstOrDefault();
            var mat = ExcelCells.Get(row, "Matricule", "MATRICULE", "Matricule SAGE")
                    ?? (row.Count > 1 ? row.Values.Skip(1).FirstOrDefault() : null);
            if (string.IsNullOrWhiteSpace(catName) || string.IsNullOrWhiteSpace(mat))
            {
                lignesVides++;
                skipped++;
                continue;
            }
            var cat = cats.FirstOrDefault(c => string.Equals(c.Intitule?.Trim(), catName.Trim(), StringComparison.OrdinalIgnoreCase));
            if (cat == null)
            {
                categoriesManquantes++;
                AjouterValeurDistincte(categoriesIntrouvables, catName);
                skipped++;
                continue;
            }
            var card = cards.FirstOrDefault(c => string.Equals(c.SageMatricule?.Trim(), mat.Trim(), StringComparison.OrdinalIgnoreCase));
            if (card == null)
            {
                matriculesManquants++;
                AjouterValeurDistincte(matriculesIntrouvables, mat);
                skipped++;
                continue;
            }
            var found = existing.FirstOrDefault(a => a.MatriculeSage == card.Id);
            if (found == null)
            {
                found = new CategorieSalarie
                {
                    MatriculeSage = card.Id,
                    IdCateg = cat.IdCategorie,
                    BddSage = _tenant.SageDb,
                    BddPointeuse = _tenant.PointeuseDb
                };
                await repo.AddAsync(found, ct);
                existing.Add(found);
            }
            else
            {
                found.IdCateg = cat.IdCategorie;
                repo.Update(found);
            }
            imported++;
        }
        await _uow.SaveChangesAsync(ct);
        var raisons = new List<string>();
        if (lignesVides > 0)
            raisons.Add($"{lignesVides} ligne(s) vide(s)");
        if (categoriesManquantes > 0)
            raisons.Add($"{categoriesManquantes} catégorie(s) introuvable(s) : {ApercuValeurs(categoriesIntrouvables)}");
        if (matriculesManquants > 0)
            raisons.Add($"{matriculesManquants} matricule(s) introuvable(s) : {ApercuValeurs(matriculesIntrouvables)}");
        var message = $"{imported} affectation(s) importée(s), {skipped} ligne(s) ignorée(s)";
        if (raisons.Count > 0)
            message += $" ({string.Join(" ; ", raisons)})";
        return new ImportResultDto(imported, skipped, message + ".");
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
