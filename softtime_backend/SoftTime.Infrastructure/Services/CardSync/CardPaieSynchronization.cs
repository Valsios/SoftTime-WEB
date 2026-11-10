using SoftTime.Application.Abstractions.ExternalData;
using SoftTime.Application.DTOs;
using SoftTime.Domain.Entities.SoftTime;
using SoftTime.Domain.Repositories;
using SoftTime.Domain.Services;

namespace SoftTime.Infrastructure.Services.CardSync;

/// <summary>
/// Une synchronisation des cartes de paie AUTO d'une base SAGE avec la pointeuse : crée ou met à
/// jour une carte par salarié retrouvé dans la pointeuse, puis met de côté les cartes orphelines
/// sans données liées.
/// </summary>
internal sealed class CardPaieSynchronization
{
    private const string OrigineAuto = "AUTO";

    private readonly IUnitOfWork _uow;
    private readonly BddSage _sageRow;
    private readonly BddPointeuse _pteRow;
    private readonly CardPaieSyncSource _source;
    private readonly List<CardPaie> _scopeCards;
    private readonly List<string> _matched = new();
    private int _added;
    private int _deactivated;

    public CardPaieSynchronization(
        IUnitOfWork uow, BddSage sageRow, BddPointeuse pteRow, CardPaieSyncSource source)
    {
        _uow = uow;
        _sageRow = sageRow;
        _pteRow = pteRow;
        _source = source;
        _scopeCards = new List<CardPaie>();
    }

    public async Task<SyncResultDto> RunAsync(CancellationToken ct)
    {
        await LoadScopeCardsAsync(ct);
        await SyncEmployeesAsync(ct);
        await DeactivateOrphansAsync(ct);
        await _uow.SaveChangesAsync(ct);
        return new SyncResultDto(_added, _deactivated,
            $"{_added} carte(s) ajoutée(s), {_deactivated} mise(s) de côté (plus de correspondance trouvée).");
    }

    /// <summary>
    /// L'identité d'une carte est le salarié (matricule) dans une base SAGE : le reste de
    /// l'application raisonne par base SAGE et non par paire de serveurs. Filtrer sur la paire
    /// recréerait un jeu complet de cartes dès que le libellé du serveur ou la base pointeuse change.
    /// </summary>
    private async Task LoadScopeCardsAsync(CancellationToken ct)
    {
        var sageDbName = (_sageRow.NomBd ?? string.Empty).Trim();
        _scopeCards.AddRange((await _uow.Repository<CardPaie>().ListAsync(_ => true, ct))
            .Where(c => string.Equals((c.SageBdd ?? string.Empty).Trim(), sageDbName, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task SyncEmployeesAsync(CancellationToken ct)
    {
        var seenEmployees = new HashSet<long>();
        foreach (var salarie in _source.Employees)
        {
            var matricule = (salarie.Matricule ?? string.Empty).Trim();
            // La source peut renvoyer plusieurs fois le même salarié (matricule formaté différemment) :
            // une seule carte doit être produite par employé.
            if (matricule.Length == 0 || !seenEmployees.Add(salarie.EmployeeId))
                continue;
            var badge = _source.Badge(salarie, matricule);
            if (badge == null)
                continue;

            _matched.Add(matricule);
            await UpsertAsync(salarie, matricule, badge, ct);
        }
    }

    private async Task UpsertAsync(ExternalEmployee salarie, string matricule, ExternalPunchUser badge, CancellationToken ct)
    {
        var departement = _source.Departement(salarie.EmployeeId);
        // Comparaison par identifiant (00369 == 369) : un simple changement de format du
        // matricule ne doit pas créer une nouvelle carte.
        var existingCard = _scopeCards.FirstOrDefault(c => MatriculeIdentity.MemeIdentifiant(c.SageMatricule, matricule));
        if (existingCard == null)
        {
            var newCard = NewCard(salarie, matricule, badge, departement);
            await _uow.Repository<CardPaie>().AddAsync(newCard, ct);
            _scopeCards.Add(newCard); // sinon la carte créée n'est pas vue plus loin dans la même passe
            _added++;
        }
        else if (existingCard.Origine == OrigineAuto)
        {
            existingCard.SageNom = salarie.Nom;
            existingCard.SagePrenom = salarie.Prenom;
            existingCard.PointeuseNumero = badge.Badge;
            existingCard.PointeuseNom = badge.Name;
            // La carte suit la configuration courante : la paire est mise à jour au lieu
            // de créer une seconde carte pour le même salarié.
            existingCard.SageServeur = _sageRow.Serveur;
            existingCard.PointeuseServeur = _pteRow.Serveur;
            existingCard.PointeuseBdd = _pteRow.NomBd;
            if (departement != null)
                existingCard.Departement = departement;
            existingCard.Actif = true;
            _uow.Repository<CardPaie>().Update(existingCard);
        }
    }

    private CardPaie NewCard(ExternalEmployee salarie, string matricule, ExternalPunchUser badge, string? departement) => new()
    {
        SageMatricule = matricule,
        SageNom = salarie.Nom,
        SagePrenom = salarie.Prenom,
        PointeuseNumero = badge.Badge,
        PointeuseNom = badge.Name,
        SageServeur = _sageRow.Serveur,
        PointeuseServeur = _pteRow.Serveur,
        SageBdd = _sageRow.NomBd,
        PointeuseBdd = _pteRow.NomBd,
        Departement = departement,
        Origine = OrigineAuto,
        Actif = true,
        Date = DateTime.Now
    };

    /// <summary>Une carte AUTO sans correspondance est désactivée, sauf si des données y sont liées.</summary>
    private async Task DeactivateOrphansAsync(CancellationToken ct)
    {
        foreach (var card in _scopeCards.Where(c => c.Origine == OrigineAuto && c.Actif).ToList())
        {
            if (_matched.Any(m => MatriculeIdentity.MemeIdentifiant(m, card.SageMatricule)))
                continue;
            if (await HasLinkedDataAsync(card, ct))
                continue; // données liées : jamais désactivée ni supprimée
            card.Actif = false;
            _uow.Repository<CardPaie>().Update(card);
            _deactivated++;
        }
    }

    private async Task<bool> HasLinkedDataAsync(CardPaie card, CancellationToken ct)
    {
        var hasPunches = (await _uow.Repository<Pointage>().ListAsync(p =>
            p.NomBddSage == _sageRow.NomBd && p.MatriculeSage == card.SageMatricule, ct)).Any();
        var hasCategorie = (await _uow.Repository<CategorieSalarie>().ListAsync(a => a.MatriculeSage == card.Id, ct)).Any();
        return hasPunches || hasCategorie;
    }
}
