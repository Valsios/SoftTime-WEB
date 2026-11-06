using SoftTime.Application.Abstractions;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;

namespace SoftTime.Infrastructure.Services;

public sealed class CorrespondenceModeService : ICorrespondenceModeService
{
    private readonly IUnitOfWork _uow;
    private readonly ITenantConnectionService _tenant;
    private readonly IExternalSourceService _externalSource;

    public CorrespondenceModeService(
        IUnitOfWork uow,
        ITenantConnectionService tenant,
        IExternalSourceService externalSource)
    {
        _uow = uow;
        _tenant = tenant;
        _externalSource = externalSource;
    }

    // Le mode correspondance est rattache a la paire
    // selectionnee par l'utilisateur
    public async Task<CorrespondenceModeDto> GetCorrespondenceAsync(CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var pteRow = await _tenant.GetPointeuseRowAsync(ct);
        var row = (await _uow.Repository<CorrespondanceActive>().ListReadOnlyAsync(a =>
            a.SageServeur == sageRow.Serveur && a.SageBdd == sageRow.NomBd &&
            a.PointeuseServeur == pteRow.Serveur && a.PointeuseBdd == pteRow.NomBd, ct)).FirstOrDefault();
        return new CorrespondenceModeDto(row?.Active == true);
    }

    public async Task<CorrespondenceModeDto> SetCorrespondenceAsync(bool active, CancellationToken ct = default)
    {
        await _tenant.EnsureAuthorizedAsync(ct);
        var sageRow = await _tenant.GetSageRowAsync(ct);
        var pteRow = await _tenant.GetPointeuseRowAsync(ct);
        var repo = _uow.Repository<CorrespondanceActive>();
        var row = (await repo.ListAsync(a =>
            a.SageServeur == sageRow.Serveur && a.SageBdd == sageRow.NomBd &&
            a.PointeuseServeur == pteRow.Serveur && a.PointeuseBdd == pteRow.NomBd, ct)).FirstOrDefault();
        if (row == null)
        {
            row = new CorrespondanceActive
            {
                Active = active,
                SageServeur = sageRow.Serveur,
                SageBdd = sageRow.NomBd,
                PointeuseServeur = pteRow.Serveur,
                PointeuseBdd = pteRow.NomBd,
                DateModif = DateTime.Now
            };
            await repo.AddAsync(row, ct);
        }
        else
        {
            row.Active = active;
            row.DateModif = DateTime.Now;
            repo.Update(row);
        }
        await _uow.SaveChangesAsync(ct);
        if (!active)
        {
            // Desactivation du mode manuel : on relance immediatement l'auto-mapping
            var result = await SyncCardPaieCoreAsync(sageRow, pteRow, ct);
            row.DerniereSync = DateTime.Now;
            repo.Update(row);
            await _uow.SaveChangesAsync(ct);
        }
        return new CorrespondenceModeDto(active);
    }

    public async Task<SyncResultDto> SyncCardPaieCoreAsync(BddSage sageRow, BddPointeuse pteRow, CancellationToken ct)
    {
        var employees = await _externalSource.GetEmployeesAsync(sageRow, ct);
        var badges = await _externalSource.GetPunchUsersAsync(pteRow, ct);
        var affectations = await _externalSource.GetAffectationsAsync(sageRow, ct);
        var departments = (await _externalSource.GetDepartmentsAsync(sageRow, ct))
            .GroupBy(d => d.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Label, StringComparer.OrdinalIgnoreCase);
        string? DepartementDe(long numSalarie)
        {
            var aff = affectations.Where(a => a.EmployeeId == numSalarie && a.DateSortie == null)
                                   .OrderByDescending(a => a.DateDebut).FirstOrDefault()
                      ?? affectations.Where(a => a.EmployeeId == numSalarie)
                                     .OrderByDescending(a => a.DateDebut).FirstOrDefault();
            if (aff?.Departement == null) return null;
            return departments.TryGetValue(aff.Departement, out var intitule) && !string.IsNullOrWhiteSpace(intitule)
                ? intitule : aff.Departement;
        }
        var repo = _uow.Repository<CardPaie>();
        // L'identite d'une carte est le salarie (matricule) dans une base SAGE : le reste de
        // l'application (ListCardsAsync, ImportFromClockAsync) raisonne par base SAGE et non par
        // paire de serveurs. Filtrer ici sur la paire recreait un jeu complet de cartes des que
        // le libelle du serveur ou la base pointeuse changeait.
        var sageDbName = (sageRow.NomBd ?? string.Empty).Trim();
        var scopeCards = (await repo.ListAsync(_ => true, ct))
            .Where(c => string.Equals((c.SageBdd ?? string.Empty).Trim(), sageDbName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        static bool MemeIdentifiant(string? a, string? b)
        {
            a = a?.Trim();
            b = b?.Trim();
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (long.TryParse(a, out var na) && long.TryParse(b, out var nb))
                return na == nb;
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }
        var matched = new List<string>();
        var seenEmployees = new HashSet<long>();
        var added = 0;
        foreach (var sal in employees)
        {
            var matricule = (sal.Matricule ?? string.Empty).Trim();
            // La source peut renvoyer plusieurs fois le meme salarie (matricule formate differemment) :
            // une seule carte doit etre produite par employe.
            if (matricule.Length == 0 || !seenEmployees.Add(sal.EmployeeId)) continue;
            var match = badges.FirstOrDefault(b =>
                MemeIdentifiant(b.Ssn, matricule)
                || MemeIdentifiant(b.Badge, matricule)
                || MemeIdentifiant(b.Badge, sal.Badge));
            if (match == null) continue;
            matched.Add(matricule);
            var departement = DepartementDe(sal.EmployeeId);
            // Comparaison par identifiant (00369 == 369) : un simple changement de format du
            // matricule ne doit pas creer une nouvelle carte.
            var existingCard = scopeCards.FirstOrDefault(c => MemeIdentifiant(c.SageMatricule, matricule));
            if (existingCard == null)
            {
                var newCard = new CardPaie
                {
                    SageMatricule = matricule,
                    SageNom = sal.Nom,
                    SagePrenom = sal.Prenom,
                    PointeuseNumero = match.Badge,
                    PointeuseNom = match.Name,
                    SageServeur = sageRow.Serveur,
                    PointeuseServeur = pteRow.Serveur,
                    SageBdd = sageRow.NomBd,
                    PointeuseBdd = pteRow.NomBd,
                    Departement = departement,
                    Origine = "AUTO",
                    Actif = true,
                    Date = DateTime.Now
                };
                await repo.AddAsync(newCard, ct);
                scopeCards.Add(newCard); // sinon la carte creee n'est pas vue plus loin dans la meme passe
                added++;
            }
            else if (existingCard.Origine == "AUTO")
            {
                existingCard.SageNom = sal.Nom;
                existingCard.SagePrenom = sal.Prenom;
                existingCard.PointeuseNumero = match.Badge;
                existingCard.PointeuseNom = match.Name;
                // La carte suit la configuration courante : la paire est mise a jour au lieu
                // de creer une seconde carte pour le meme salarie.
                existingCard.SageServeur = sageRow.Serveur;
                existingCard.PointeuseServeur = pteRow.Serveur;
                existingCard.PointeuseBdd = pteRow.NomBd;
                if (departement != null) existingCard.Departement = departement;
                existingCard.Actif = true;
                repo.Update(existingCard);
            }
        }
        var deactivated = 0;
        foreach (var card in scopeCards.Where(c => c.Origine == "AUTO" && c.Actif))
        {
            if (matched.Any(m => MemeIdentifiant(m, card.SageMatricule))) continue;
            var hasPunches = (await _uow.Repository<Pointage>().ListAsync(p =>
                p.NomBddSage == sageRow.NomBd && p.MatriculeSage == card.SageMatricule, ct)).Any();
            var hasCategorie = (await _uow.Repository<CategorieSalarie>().ListAsync(a => a.MatriculeSage == card.Id, ct)).Any();
            if (hasPunches || hasCategorie) continue; // donnees liees : jamais desactivee ni supprimee
            card.Actif = false;
            repo.Update(card);
            deactivated++;
        }
        await _uow.SaveChangesAsync(ct);
        return new SyncResultDto(added, deactivated,
            $"{added} carte(s) ajoutée(s), {deactivated} mise(s) de côté (plus de correspondance trouvée).");
    }
}
