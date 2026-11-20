import { AfterViewInit, ChangeDetectionStrategy, Component, computed, ElementRef, inject, OnInit, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ConfirmService } from '../../core/confirm.service';
import { errorMessage } from '../../core/error.logic';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';
import { ToastService } from '../../core/toast.service';
import { PayrollDomainState, PayrollPeriods, PayrollPeriodState } from '../../shared/models';
import { PayrollClosureService } from '../../shared/services/payroll-closure.service';
import { SoftBadge, SoftButton, SoftCard } from '../../shared/components';
import {
  ANCRE_CLOTURE,
  aujourdhuiIso,
  avertissement,
  dateHeureFr,
  empreinteCourte,
  libelleEtat,
  libelleEtatDomaine,
  libelleFiltre,
  libellePeriode,
  peutVoirCloture,
  questionCloture,
  TITRE_CARTE_CLOTURE,
  varianteDomaine,
} from './payroll-closure.logic';

/**
 * « Clôture de paie » de la base SAGE active, sous les variables envoyées à la paie (même écran, mêmes droits de base) :
 * périodes envoyées avec leur état, empreinte de l'envoi figé, bouton « Clôturer » (droit SAGE:{base}:CLOTURE, ou
 * administrateur). La clôture est définitive : aucun bouton ni appel de déblocage n'existe. Le refus du serveur
 * (Contrôle avant paie non « Prêt », envoi filtré, période non terminée…) est affiché tel quel.
 */
@Component({
  selector: 'app-payroll-closure',
  host: { '[id]': 'ancre' },
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SoftBadge, SoftButton, SoftCard],
  template: `
    <!-- Sans base active ou sans droit (modifier ou clôturer la base), la carte n'apparaît pas. -->
    @if (base() && visible()) {
    <soft-card>
      <div class="pc-head">
        <div>
          <h2 class="pc-title">{{ titre }}</h2>
          <p class="pc-sub">Base active : <strong>{{ base() }}</strong></p>
        </div>
        <soft-button variant="ghost" size="sm" [disabled]="loading()" (click)="charger()">Actualiser</soft-button>
      </div>

        <p class="pc-note">
          La clôture fige une période déjà envoyée en paie : les valeurs envoyées sont conservées avec leur empreinte et plus
          aucune donnée de la période ne peut être modifiée (pointages, heures corrigées, anomalies, planning, validation,
          calcul, nouvel envoi). Elle exige un envoi sans filtre réussi et le Contrôle avant paie « Prêt » sur la base entière,
          ainsi qu'un envoi sans plage de matricules, sur les bornes exactes de la période, de la cantine et des frais de
          déplacement quand la base les envoie à la paie. Elle est définitive : il n'existe pas de retour en arrière, les
          corrections se font sur la période suivante.
        </p>
        @if (erreur(); as e) {
          <div class="pc-banner pc-banner--danger" role="alert"><p>{{ e }}</p></div>
        }
        @if (loading() && !donnees()) {
          <p class="pc-note">Chargement…</p>
        } @else if (donnees(); as d) {
          @if (!d.periodes.length) {
            <p class="pc-note">Aucune période envoyée en paie pour cette base.</p>
          } @else {
            <div class="soft-table-wrap pc-wrap">
              <table class="soft-table pc-table">
                <thead>
                  <tr>
                    <th>Période</th>
                    <th>État</th>
                    <th>Domaines</th>
                    <th>Dernier envoi</th>
                    <th>Empreinte</th>
                    <th><span class="pc-sr">Action</span></th>
                  </tr>
                </thead>
                <tbody>
                  @for (p of d.periodes; track p.periodeDebut + p.periodeFin) {
                    <tr [class.pc-closed]="p.etat === 'CLOTUREE'">
                      <td class="pc-period">{{ periode(p) }}</td>
                      <td data-label="État">
                        <soft-badge [variant]="p.etat === 'CLOTUREE' ? 'success' : 'info'">{{ etat(p) }}</soft-badge>
                        @if (p.cloture; as c) {
                          <div class="pc-meta">le {{ quand(c.clotureLe) }} par {{ c.cloturePar }}</div>
                        }
                      </td>
                      <td data-label="Domaines">
                        @if (p.domaines?.length) {
                          <ul class="pc-domains" [attr.aria-label]="'Domaines de la période ' + periode(p)">
                            @for (d of p.domaines; track d.domaine) {
                              <li class="pc-domain">
                                <span class="pc-domain__name">{{ d.libelle }}<span class="pc-sr"> : </span></span>
                                <soft-badge [variant]="variante(d)">{{ etatDomaine(d) }}</soft-badge>
                                @if (d.empreinte && d.etat !== 'NON_EXIGE') {
                                  <span class="pc-hash pc-hash--small" [attr.title]="d.empreinte">{{ court(d.empreinte) }}</span>
                                }
                                @if (d.motif) { <div class="pc-meta pc-domain__why">{{ d.motif }}</div> }
                              </li>
                            }
                          </ul>
                        } @else {
                          <span class="pc-meta">Heures seulement</span>
                        }
                      </td>
                      <td data-label="Dernier envoi">
                        <div>{{ quand(p.dernierEnvoi.envoyeLe) }} — {{ p.dernierEnvoi.envoyePar || 'SYSTEME' }}</div>
                        <div class="pc-meta">
                          {{ p.dernierEnvoi.statut }}@if (p.dernierEnvoi.nbLignes != null) { · {{ p.dernierEnvoi.nbLignes }} valeur(s) figée(s) }
                          @if (filtre(p); as f) { · filtré : {{ f }} }
                        </div>
                      </td>
                      <td data-label="Empreinte">
                        @if (empreinte(p); as e) {
                          <span class="pc-hash" [attr.title]="e">{{ court(e) }}</span>
                          <soft-button size="sm" variant="ghost" [ariaLabel]="'Copier l’empreinte de la période ' + periode(p)" (click)="copier(e)">Copier</soft-button>
                        } @else {
                          <span class="pc-meta">aucune (envoi sans instantané)</span>
                        }
                      </td>
                      <td class="pc-action" data-label="Action">
                        @if (p.etat === 'ENVOYEE' && d.peutCloturer) {
                          <soft-button variant="danger" size="sm" [loading]="enCours() === cle(p)" [disabled]="enCours() !== null"
                            [ariaLabel]="'Clôturer la période ' + periode(p)" (click)="cloturer(p)">Clôturer</soft-button>
                          @if (aide(p); as a) { <div class="soft-field__hint pc-hint">{{ a }}</div> }
                        } @else if (p.etat === 'ENVOYEE') {
                          <span class="pc-meta">Clôture réservée au droit « Clôturer la paie » de la base.</span>
                        } @else {
                          <span class="pc-meta">Définitive</span>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          }
        }
    </soft-card>
    }
  `,
  styles: [`
    :host { display: block; }
    soft-card { display: block; margin-top: var(--space-lg); }
    .pc-head { display: flex; justify-content: space-between; align-items: flex-start; gap: var(--space-md); flex-wrap: wrap;
      margin-bottom: var(--space-md); }
    .pc-title { margin: 0; font-family: var(--font-heading); font-size: 16px; font-weight: 600; color: var(--app-text-strong); }
    .pc-sub { margin: var(--space-xs) 0 0; color: var(--text-secondary); font-size: 14px; }
    .pc-note { color: var(--text-muted); margin: var(--space-md) 0; font-size: 13px; }
    .pc-banner { margin: var(--space-md) 0; padding: 10px 14px; font-size: 13px; border-radius: var(--radius-md); border: 1px solid; }
    .pc-banner p { margin: 2px 0; overflow-wrap: anywhere; }
    .pc-banner--danger { color: var(--danger); background: var(--danger-bg); border-color: var(--danger-border); }
    .pc-banner--info { color: var(--info); background: var(--info-bg); border-color: var(--info-border); }
    .pc-table td { vertical-align: top; }
    .pc-period { font-weight: 600; white-space: nowrap; color: var(--app-text-strong); }
    .pc-meta { color: var(--text-muted); font-size: 12px; margin-top: 2px; overflow-wrap: anywhere; }
    .pc-hash { font-family: var(--font-mono, ui-monospace, SFMono-Regular, Menlo, Consolas, monospace); font-size: 13px;
      color: var(--app-text); margin-right: var(--space-xs); }
    .pc-action { min-width: 150px; }
    .pc-domains { list-style: none; margin: 0; padding: 0; display: grid; gap: 6px; min-width: 180px; }
    .pc-domain { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 8px; min-width: 0; }
    .pc-domain__name { font-size: 13px; color: var(--app-text); min-width: 0; }
    .pc-domain__why { flex-basis: 100%; max-width: 320px; }
    .pc-hash--small { font-size: 12px; color: var(--text-muted); margin-right: 0; }
    .pc-hint { margin-top: var(--space-xs); max-width: 260px; }
    .pc-sr { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
    /* Largeur étroite : une carte par période (comme la carte des variables), étiquette à gauche, valeur à droite. */
    @media (max-width: 767px) {
      .soft-table-wrap.pc-wrap { display: block; overflow: visible; border: 0; border-radius: 0; box-shadow: none; background: transparent; }
      .pc-table, .pc-table tbody, .pc-table tr, .pc-table td { display: block; }
      .pc-table thead { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
      .pc-table tbody { display: grid; gap: 12px; }
      .pc-table tbody tr { display: grid; gap: 10px; padding: 16px; border: 1px solid var(--color-border); border-radius: var(--radius-lg);
        background: var(--app-surface); box-shadow: var(--shadow-xs); }
      .pc-table tbody tr:hover { background: var(--app-surface); }
      .pc-table tbody td { display: grid; grid-template-columns: minmax(0, 36%) minmax(0, 1fr); gap: 4px 12px; align-items: start;
        min-width: 0; padding: 0; border: 0; }
      .pc-table tbody td::before { content: attr(data-label); color: var(--text-secondary); font-size: 12px; font-weight: 600; }
      .pc-table tbody td > * { grid-column: 2; min-width: 0; }
      .pc-table tbody td.pc-period { display: block; white-space: normal; }
      .pc-table tbody td.pc-period::before { content: none; }
      .pc-action { min-width: 0; }
      .pc-hint { max-width: none; }
      .pc-domains { min-width: 0; }
      .pc-domain__why { max-width: none; }
    }
  `],
})
export class PayrollClosureCard implements OnInit, AfterViewInit {
  readonly titre = TITRE_CARTE_CLOTURE;
  /** Ancre visée par le lien « Aller à la clôture » du Contrôle avant paie. */
  readonly ancre = ANCRE_CLOTURE;
  private readonly route = inject(ActivatedRoute, { optional: true });
  private readonly hote = inject(ElementRef<HTMLElement>);
  private readonly svc = inject(PayrollClosureService);
  private readonly session = inject(SessionStore);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(false);
  readonly donnees = signal<PayrollPeriods | null>(null);
  /** Refus ou erreur du serveur, affiché tel quel jusqu'à la prochaine action. */
  readonly erreur = signal<string | null>(null);
  /** Clé de la période en cours de clôture. */
  readonly enCours = signal<string | null>(null);

  readonly base = computed(() => this.session.activeSageDb());

  /** Consultation : droit de modifier OU de clôturer la base (le serveur vérifie de nouveau). */
  readonly visible = computed(() => {
    return peutVoirCloture(this.base(), this.session.permissions(), this.session.hasRight(Droit.Databases));
  });

  ngOnInit(): void {
    this.charger();
  }

  /** Arrivée par le lien du Contrôle (fragment #cloture-paie) : la carte est amenée à l'écran. */
  ngAfterViewInit(): void {
    if (this.route?.snapshot.fragment !== ANCRE_CLOTURE) return;
    setTimeout(() => (this.hote.nativeElement as HTMLElement).scrollIntoView?.({ block: 'start' }));
  }

  charger(): void {
    if (!this.base() || !this.visible()) return;
    this.loading.set(true);
    this.svc.periods().subscribe({
      next: (d) => { this.donnees.set(d); this.loading.set(false); },
      error: (e) => { this.loading.set(false); this.erreur.set(errorMessage(e)); },
    });
  }

  periode(p: PayrollPeriodState): string { return libellePeriode(p); }
  etat(p: PayrollPeriodState): string { return libelleEtat(p.etat); }
  quand(iso: string): string { return dateHeureFr(iso); }
  filtre(p: PayrollPeriodState): string { return libelleFiltre(p.dernierEnvoi); }
  court(e: string): string { return empreinteCourte(e); }
  cle(p: PayrollPeriodState): string { return `${p.periodeDebut}|${p.periodeFin}`; }
  aide(p: PayrollPeriodState): string | null { return avertissement(p, aujourdhuiIso()); }
  etatDomaine(d: PayrollDomainState): string { return libelleEtatDomaine(d); }
  variante(d: PayrollDomainState): 'success' | 'info' | 'warning' | 'muted' { return varianteDomaine(d); }

  /** Empreinte de la clôture (figée) ou du dernier envoi. */
  empreinte(p: PayrollPeriodState): string | null {
    return p.cloture?.empreinte ?? p.dernierEnvoi.empreinte ?? null;
  }

  async copier(empreinte: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(empreinte);
      this.toast.success('Empreinte copiée.');
    } catch {
      this.toast.info(`Empreinte : ${empreinte}`);
    }
  }

  async cloturer(p: PayrollPeriodState): Promise<void> {
    const base = this.base();
    if (!base || this.enCours()) return;
    this.erreur.set(null);
    if (!(await this.confirm.ask(questionCloture(base, p), 'Clôturer la période de paie', 'Clôturer définitivement'))) return;
    this.enCours.set(this.cle(p));
    this.svc.close(p.periodeDebut.slice(0, 10), p.periodeFin.slice(0, 10)).subscribe({
      next: (c) => {
        this.enCours.set(null);
        this.toast.success(`Période ${libellePeriode(p)} clôturée (empreinte ${empreinteCourte(c.empreinte)}).`);
        this.charger();
      },
      error: (e) => {
        this.enCours.set(null);
        this.erreur.set(errorMessage(e));
      },
    });
  }
}
