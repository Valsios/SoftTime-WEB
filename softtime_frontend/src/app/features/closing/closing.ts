import { HttpErrorResponse } from '@angular/common/http';
import {
  ChangeDetectionStrategy,
  Component,
  computed,
  inject,
  OnDestroy,
  OnInit,
  signal,
  ViewChild,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { errorMessage } from '../../core/error.logic';
import { SessionStore } from '../../core/session.store';
import { PageHeader, PeriodFilter, SoftBadge, SoftCard, SoftTabs, SoftToggle, TabItem } from '../../shared/components';
import { ClosingAnomalyView, ClosingReport, PeriodRequest } from '../../shared/models';
import { ClosingService } from '../../shared/services/closing.service';
import {
  correctionLink,
  CorrectionLink,
  countOf,
  DEFAULT_PAGE_SIZE,
  defaultPeriod,
  DISABLED_MESSAGE,
  excludedEmployeesText,
  formatDay,
  INFORMATION_RULE,
  isDisabledError,
  NO_ANOMALY,
  NO_CONTROLLED_DAY,
  NOTHING_TO_FIX,
  nothingToFix,
  PAGE_SIZES,
  personLabel,
  PURPOSE_NOTE,
  SEVERITIES,
  severityLabel,
  severitySentence,
  severityTone,
  scoreText,
  statusRuleText,
  statusView,
  summarySentences,
  typeLabel,
} from './closing.logic';

/**
 * Clôture de paie assistée : état de préparation d'une période (statut, score expliqué, anomalies à
 * corriger). Page en lecture seule : elle affiche la réponse de GET api/closing/analysis et propose
 * un lien vers la page où chaque anomalie se corrige.
 */
@Component({
  selector: 'app-closing',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, PeriodFilter, SoftCard, SoftBadge, SoftTabs, SoftToggle, RouterLink],
  template: `
    <page-header title="Clôture de paie" subtitle="Contrôle des données de la période avant la clôture">
      <label class="closing-switch">
        <span>Comparer avec la période précédente</span>
        <soft-toggle [ngModel]="includeComparison()" (ngModelChange)="onComparison($event)" name="comparaison"></soft-toggle>
        <span class="closing-switch__state">{{ comparisonState() }}</span>
      </label>
    </page-header>

    <p class="closing-banner" role="note">{{ purposeNote }}</p>

    <period-filter
      [showMatricule]="false"
      [showDepartement]="true"
      searchLabel="Analyser"
      (search)="onSearch($event)"
    ></period-filter>

    @if (loading() && !report()) {
      <div class="soft-empty" role="status">
        <div class="soft-spinner-lg"></div>
        Analyse de la période en cours…
      </div>
    } @else if (disabled()) {
      <soft-card variant="bordered"><div class="soft-empty" role="status">{{ disabledMessage }}</div></soft-card>
    } @else if (error()) {
      <div class="closing-alert closing-alert--error" role="alert">{{ error() }}</div>
    } @else if (report(); as r) {
      <section class="closing-overview" aria-label="Synthèse de la période">
        <soft-card variant="bordered">
          <div class="closing-status">
            <span class="closing-kpi__label">Statut</span>
            <soft-badge [variant]="status().tone">{{ status().label }}</soft-badge>
            <span class="closing-status__hint">{{ status().hint }}</span>
          </div>
          <div class="closing-score">
            <span class="closing-kpi__label">Score</span>
            <strong class="closing-score__value">{{ score() }}</strong>
          </div>
          <p class="closing-period">
            Du {{ day(r.from) }} au {{ day(r.to) }}{{ r.departement ? ' · département ' + r.departement : '' }}
            · {{ r.joursEmployeControles }} jour(s)-employé contrôlé(s)
          </p>
          @if (excluded(); as texte) {
            <p class="closing-period closing-excluded" role="note">{{ texte }}</p>
          }
          <details class="closing-explain">
            <summary>Comment le score et le statut sont calculés</summary>
            <p>{{ r.formule }}</p>
            <p>Chaque anomalie pèse selon sa gravité (poids indiqué dans le tableau ci-dessous).</p>
            <p>{{ statusRule() }}</p>
            <p>{{ informationRule }}</p>
          </details>
        </soft-card>

        <div class="closing-kpis">
          @for (s of severities; track s) {
            <soft-card variant="bordered">
              <div class="closing-count">
                <span class="closing-kpi__label"><soft-badge [variant]="tone(s)">{{ severityText(s) }}</soft-badge></span>
                <strong>{{ count(r, s) }}</strong>
                <span class="closing-count__text">{{ sentence(s, count(r, s)) }}</span>
              </div>
            </soft-card>
          }
        </div>
      </section>

      <soft-card variant="bordered" padding="sm">
        <div class="closing-section-head"><h2>Résumé</h2></div>
        <ul class="closing-summary">
          @for (p of summary(); track $index) {
            <li>{{ p }}</li>
          }
        </ul>
      </soft-card>

      @if (r.joursEmployeControles > 0) {
        <soft-card variant="bordered" padding="sm">
          <div class="closing-section-head">
            <h2>Par département</h2>
            <span>{{ r.parDepartement.length }} département(s)</span>
          </div>
          <div class="soft-table-wrap">
            <table class="soft-table">
              <caption class="closing-sr">Score et anomalies par département</caption>
              <thead>
                <tr>
                  <th scope="col">Département</th>
                  <th scope="col">Score</th>
                  <th scope="col">Jours contrôlés</th>
                  <th scope="col">Anomalies</th>
                  <th scope="col">Critiques</th>
                </tr>
              </thead>
              <tbody>
                @for (d of r.parDepartement; track d.departement) {
                  <tr>
                    <th scope="row">{{ d.departement || 'Sans département' }}</th>
                    <td>{{ scoreLabel(d.score) }}</td>
                    <td>{{ d.joursEmployeControles }}</td>
                    <td>{{ d.nombreAnomalies }}</td>
                    <td>{{ countKey(d.parGravite, 'Critique') }}</td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        </soft-card>

        <soft-card variant="bordered" padding="sm">
          <div class="closing-section-head">
            <h2>Anomalies à corriger</h2>
          </div>
          <soft-tabs [tabs]="tabs()" [activeTab]="tab()" (activeTabChange)="onTab($event)"></soft-tabs>
          @if (r.anomalies.total === 0 && r.nombreAnomalies === 0) {
            <div class="closing-state closing-state--success" role="status">{{ noAnomaly }}</div>
          } @else if (r.anomalies.total === 0) {
            <div class="closing-state closing-state--success" role="status">{{ hiddenInformationText() }}</div>
          } @else {
            @if (!r.nomsAffiches) {
              <p class="closing-muted">Les noms ne sont pas affichés sur ce serveur : matricule seul.</p>
            }
            <div class="soft-table-wrap">
              <table class="soft-table">
                <caption class="closing-sr">Anomalies de la période, page {{ r.anomalies.page }} sur {{ r.anomalies.totalPages }}</caption>
                <thead>
                  <tr>
                    <th scope="col">Gravité</th>
                    <th scope="col">Type</th>
                    <th scope="col">Matricule</th>
                    <th scope="col">Salarié</th>
                    <th scope="col">Département</th>
                    <th scope="col">Date</th>
                    <th scope="col">Détail</th>
                    <th scope="col">Correction</th>
                  </tr>
                </thead>
                <tbody>
                  @for (a of r.anomalies.items; track $index) {
                    <tr>
                      <td><soft-badge [variant]="tone(a.gravite)">{{ severityText(a.gravite) }}</soft-badge></td>
                      <td>{{ type(a.type) }}</td>
                      <td>{{ a.matricule }}</td>
                      <td>{{ person(a) }}</td>
                      <td>{{ a.departement || '—' }}</td>
                      <td>{{ day(a.date) }}</td>
                      <td>{{ a.detail }}</td>
                      <td>
                        @if (link(a); as l) {
                          @if (canOpen(l)) {
                            <a class="soft-btn soft-btn--ghost soft-btn--sm closing-fix" [routerLink]="l.path" [queryParams]="l.queryParams" [attr.aria-label]="l.ariaLabel">
                              Corriger
                            </a>
                          } @else {
                            <span class="closing-muted">Accès « {{ l.label }} » requis</span>
                          }
                        } @else if (noFix(a)) {
                          <span class="closing-muted">{{ nothingToFixText }}</span>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            <div class="soft-pagination-wrap">
              <div class="soft-pagination-info" aria-live="polite">{{ rangeInfo() }}</div>
              <label class="soft-pagination-size">
                Lignes par page
                <select [value]="pageSize()" (change)="onPageSize($event)">
                  @for (size of pageSizes; track size) {
                    <option [value]="size" [selected]="size === pageSize()">{{ size }}</option>
                  }
                </select>
              </label>
              <nav class="soft-pagination-nav" aria-label="Pagination des anomalies">
                <button type="button" class="soft-page-btn" [disabled]="page() <= 1 || loading()" (click)="goTo(1)" aria-label="Première page">&laquo;</button>
                <button type="button" class="soft-page-btn" [disabled]="page() <= 1 || loading()" (click)="goTo(page() - 1)" aria-label="Page précédente">&lsaquo;</button>
                <span class="soft-pagination-info">Page {{ page() }} / {{ r.anomalies.totalPages }}</span>
                <button type="button" class="soft-page-btn" [disabled]="page() >= r.anomalies.totalPages || loading()" (click)="goTo(page() + 1)" aria-label="Page suivante">&rsaquo;</button>
                <button type="button" class="soft-page-btn" [disabled]="page() >= r.anomalies.totalPages || loading()" (click)="goTo(r.anomalies.totalPages)" aria-label="Dernière page">&raquo;</button>
              </nav>
            </div>
          }
        </soft-card>
      } @else {
        <soft-card variant="bordered"><div class="soft-empty" role="status">{{ noControlledDay }}.</div></soft-card>
      }
    }
  `,
  styles: [
    `
      :host { display: flex; flex-direction: column; gap: 16px; }
      /* Les blocs partagés portent leurs propres marges basses : l'espacement de la page vient du « gap » de l'hôte. */
      :host ::ng-deep .soft-page-header { margin-bottom: 0; }
      :host ::ng-deep .soft-toolbar { margin-bottom: 0; }
      .closing-banner {
        margin: 0; padding: 10px 14px; font-size: 13px;
        border-radius: var(--radius-md); border: 1px solid var(--info-border);
        background: var(--info-bg); color: var(--info);
      }
      .closing-alert { padding: 10px 14px; font-size: 13px; border-radius: var(--radius-md); border: 1px solid; }
      .closing-alert--error { color: var(--danger); background: var(--danger-bg); border-color: var(--danger-border); }
      .closing-switch { display: inline-flex; align-items: center; gap: 10px; font-size: 14px; color: var(--app-text); cursor: pointer; }
      .closing-switch__state { min-width: 72px; font-size: 12.5px; font-weight: 600; color: var(--text-secondary); }
      .closing-switch:focus-within ::ng-deep .soft-toggle__track { outline: 2px solid var(--focus-ring); outline-offset: 2px; }
      .closing-fix:focus-visible, .closing-explain summary:focus-visible { outline: 2px solid var(--focus-ring); outline-offset: 2px; }
      .closing-state { padding: 14px 16px; border-radius: var(--radius-md); border: 1px solid; font-size: 14px; }
      .closing-state--success { color: var(--success); background: var(--success-bg); border-color: var(--success-border); }
      .closing-overview { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 3fr); gap: 16px; align-items: start; }
      @media (max-width: 900px) { .closing-overview { grid-template-columns: minmax(0, 1fr); } }
      .closing-status, .closing-score { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin-bottom: 8px; }
      .closing-kpi__label { font-size: 12px; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.04em; min-width: 52px; }
      .closing-status__hint { color: var(--text-secondary); font-size: 13px; }
      .closing-score__value { font-size: 22px; font-family: var(--font-heading); color: var(--app-text-strong); }
      .closing-period { margin: 4px 0 8px; color: var(--text-secondary); font-size: 13px; }
      .closing-explain summary { cursor: pointer; color: var(--accent); font-size: 13px; }
      .closing-explain p { margin: 8px 0 0; font-size: 13px; color: var(--app-text); }
      .closing-kpis { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 12px; }
      .closing-count { display: flex; flex-direction: column; gap: 6px; }
      .closing-count strong { font-size: 22px; font-family: var(--font-heading); color: var(--app-text-strong); }
      .closing-count__text { color: var(--text-secondary); font-size: 12.5px; }
      .closing-section-head { display: flex; align-items: baseline; justify-content: space-between; flex-wrap: wrap; gap: 12px; padding: 8px 8px 12px; }
      .closing-section-head h2 { margin: 0; font-family: var(--font-heading); font-size: 16px; font-weight: 600; color: var(--app-text-strong); }
      .closing-section-head span { font-size: 12.5px; color: var(--text-muted); }
      .closing-summary { margin: 0; padding: 0 8px 8px 28px; display: grid; gap: 4px; color: var(--app-text); font-size: 14px; }
      .closing-muted { color: var(--text-muted); font-size: 13px; }
      .closing-sr {
        position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px;
        overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; border: 0;
      }
    `,
  ],
})
export class ClosingPage implements OnInit, OnDestroy {
  private readonly closing = inject(ClosingService);
  private readonly session = inject(SessionStore);

  /** Filtre partagé, positionné sur le mois précédent avant son premier rendu. */
  @ViewChild(PeriodFilter, { static: true }) private filter?: PeriodFilter;

  readonly pageSizes = PAGE_SIZES;
  readonly severities = SEVERITIES;
  readonly purposeNote = PURPOSE_NOTE;
  readonly disabledMessage = DISABLED_MESSAGE;
  readonly noAnomaly = NO_ANOMALY;
  readonly informationRule = INFORMATION_RULE;
  readonly nothingToFixText = NOTHING_TO_FIX;
  readonly noControlledDay = NO_CONTROLLED_DAY;

  readonly report = signal<ClosingReport | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly disabled = signal(false);
  readonly includeComparison = signal(false);
  /** Onglet de la liste : « afix » (par défaut) ou « all » (avec les informations, includeInformation = true). */
  readonly tab = signal<'afix' | 'all'>('afix');
  readonly includeInformation = computed(() => this.tab() === 'all');
  readonly page = signal(1);
  readonly pageSize = signal<number>(DEFAULT_PAGE_SIZE);

  private period = defaultPeriod();
  private departement: string | null = null;
  private request?: Subscription;

  readonly status = computed(() => statusView(this.report()?.statut));
  readonly score = computed(() => scoreText(this.report()?.score));
  readonly statusRule = computed(() => statusRuleText(this.report()?.seuilBloquant ?? 0));
  readonly summary = computed(() => {
    const r = this.report();
    return r ? summarySentences(r) : [];
  });
  readonly comparisonState = computed(() => (this.includeComparison() ? 'Activée' : 'Désactivée'));
  /** Totaux des onglets, tirés des comptages (indépendants de l'onglet choisi). */
  readonly tabs = computed<TabItem[]>(() => {
    const r = this.report();
    const total = r?.nombreAnomalies ?? 0;
    const informations = countOf(r?.parGravite, 'Information');
    return [
      { id: 'afix', label: `À corriger (${total - informations})` },
      { id: 'all', label: `Avec informations (${total})` },
    ];
  });
  readonly excluded = computed(() => excludedEmployeesText(this.report()?.salariesSansPlanningExclus));
  /** Liste vide parce que seules des informations existent (masquées) : message calme, jamais « aucune anomalie ». */
  readonly hiddenInformationText = computed(() => {
    const n = countOf(this.report()?.parGravite, 'Information');
    return `Aucune anomalie à corriger. ${n} ${n > 1 ? 'informations masquées' : 'information masquée'} : ouvrez l’onglet « Avec informations » pour ${n > 1 ? 'les' : 'la'} voir.`;
  });
  readonly rangeInfo = computed(() => {
    const p = this.report()?.anomalies;
    if (!p || p.total === 0) return '';
    const debut = (p.page - 1) * p.pageSize + 1;
    return `${debut}–${Math.min(p.page * p.pageSize, p.total)} sur ${p.total} anomalie(s)`;
  });

  ngOnInit(): void {
    if (this.filter) {
      this.filter.from = this.period.from;
      this.filter.to = this.period.to;
    }
    this.load();
  }

  ngOnDestroy(): void {
    this.request?.unsubscribe();
  }

  onSearch(req: PeriodRequest): void {
    this.period = { from: req.from, to: req.to };
    this.departement = req.departement || null;
    this.page.set(1);
    this.load();
  }

  onComparison(active: boolean): void {
    this.includeComparison.set(!!active);
    this.load();
  }

  /** Changer d'onglet relance l'analyse à la page 1 en conservant période, département et comparaison. */
  onTab(id: string): void {
    const next = id === 'all' ? 'all' : 'afix';
    if (next === this.tab()) return;
    this.tab.set(next);
    this.page.set(1);
    this.load();
  }

  onPageSize(event: Event): void {
    this.pageSize.set(Number((event.target as HTMLSelectElement).value) || DEFAULT_PAGE_SIZE);
    this.page.set(1);
    this.load();
  }

  goTo(page: number): void {
    if (page < 1 || page === this.page()) return;
    this.page.set(page);
    this.load();
  }

  /** L'intercepteur affiche déjà l'erreur en notification : la page ne montre qu'un état, sans second message. */
  private load(): void {
    this.request?.unsubscribe();
    this.loading.set(true);
    this.error.set(null);
    this.disabled.set(false);
    this.request = this.closing
      .analysis({
        from: this.period.from,
        to: this.period.to,
        departement: this.departement,
        includeComparison: this.includeComparison(),
        includeInformation: this.includeInformation(),
        page: this.page(),
        pageSize: this.pageSize(),
      })
      .subscribe({
        next: (r) => {
          this.report.set(r);
          this.page.set(r.anomalies.page);
          this.loading.set(false);
        },
        error: (err: HttpErrorResponse) => {
          this.report.set(null);
          this.loading.set(false);
          if (isDisabledError(err)) this.disabled.set(true);
          else this.error.set(errorMessage(err));
        },
      });
  }

  count(r: ClosingReport, severity: string): number {
    return countOf(r.parGravite, severity);
  }

  countKey(counts: ClosingReport['parGravite'], key: string): number {
    return countOf(counts, key);
  }

  sentence(severity: string, count: number): string {
    return severitySentence(severity, count);
  }

  tone(severity: string) {
    return severityTone(severity);
  }

  severityText(severity: string): string {
    return severityLabel(severity);
  }

  type(t: string): string {
    return typeLabel(t);
  }

  person(a: ClosingAnomalyView): string {
    return personLabel(a);
  }

  day(value: string): string {
    return formatDay(value);
  }

  scoreLabel(score: number | null): string {
    return score === null ? 'Aucun jour contrôlé' : `${score} / 100`;
  }

  link(a: ClosingAnomalyView): CorrectionLink | null {
    return correctionLink(a);
  }

  noFix(a: ClosingAnomalyView): boolean {
    return nothingToFix(a);
  }

  /** Lien actif seulement si la page cible est accessible (même règle que le menu). */
  canOpen(l: CorrectionLink): boolean {
    return this.session.hasPermission(l.permission, l.droit);
  }
}
