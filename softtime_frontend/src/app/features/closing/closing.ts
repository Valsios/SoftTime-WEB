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
import { Column, DataTable, DataTableCell, PageHeader, PeriodFilter, SoftBadge, SoftCard, SoftTabs, SoftToggle, TabItem } from '../../shared/components';
import { ClosingAnomalyView, ClosingReport, PageChangeEvent, PeriodRequest } from '../../shared/models';
import { ClosingService } from '../../shared/services/closing.service';
import { ClosingSessionState } from './closing.state';
import {
  ANALYSING_TEXT,
  ClosingFilter,
  correctionLink,
  CorrectionLink,
  countOf,
  DEFAULT_PAGE_SIZE,
  defaultPeriod,
  DISABLED_MESSAGE,
  excludedEmployeesText,
  FILTER_CHANGED_TEXT,
  formatDay,
  INFORMATION_RULE,
  isDisabledError,
  NO_ANOMALY,
  NO_CONTROLLED_DAY,
  NOT_ANALYSED_TEXT,
  NOTHING_TO_FIX,
  nothingToFix,
  personLabel,
  PURPOSE_NOTE,
  resultBannerText,
  sameFilter,
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
  imports: [FormsModule, PageHeader, PeriodFilter, SoftCard, SoftBadge, SoftTabs, SoftToggle, RouterLink, DataTable, DataTableCell],
  template: `
    <page-header title="Contrôle avant paie" subtitle="Contrôle des données de la période avant la clôture">
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
      [busy]="analysing()"
      (changed)="refreshDraft()"
      (search)="onSearch($event)"
    ></period-filter>

    <div class="closing-status-line" role="status" aria-live="polite">
      @if (analysing()) {
        <span class="closing-busy"><span class="closing-busy__dot" aria-hidden="true"></span>{{ analysingText }}</span>
      } @else if (stale()) {
        <span class="closing-stale">{{ staleText }}</span>
      } @else if (banner(); as texte) {
        <span class="closing-muted">{{ texte }}</span>
      }
    </div>

    @if (disabled()) {
      <soft-card variant="bordered"><div class="soft-empty" role="status">{{ disabledMessage }}</div></soft-card>
    }
    @if (error()) {
      <div class="closing-alert closing-alert--error" role="alert">{{ error() }}</div>
    }
    @if (!report() && !analysing() && !disabled() && !error()) {
      <soft-card variant="bordered"><div class="soft-empty" role="status">{{ notAnalysedText }}</div></soft-card>
    }
    @if (report(); as r) {
     <div class="closing-results" [attr.aria-busy]="analysing()">

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
            <data-table
              [columns]="columns"
              [rows]="rows()"
              [caption]="anomaliesCaption()"
              [searchable]="false"
              [serverSide]="true"
              [total]="r.anomalies.total"
              [serverPage]="page()"
              [serverPageSize]="pageSize()"
              [loading]="paging()"
              (pageChange)="onPageChange($event)"
            >
              <ng-template dataTableCell="gravite" let-row>
                <soft-badge [variant]="tone(view(row).gravite)">{{ severityText(view(row).gravite) }}</soft-badge>
              </ng-template>
              <ng-template dataTableCell="correction" let-row>
                @if (link(view(row)); as l) {
                  @if (canOpen(l)) {
                    <a class="soft-btn soft-btn--ghost soft-btn--sm closing-fix" [routerLink]="l.path" [queryParams]="l.queryParams" [attr.aria-label]="l.ariaLabel">
                      Corriger
                    </a>
                  } @else {
                    <span class="closing-muted">Accès « {{ l.label }} » requis</span>
                  }
                } @else if (noFix(view(row))) {
                  <span class="closing-muted">{{ nothingToFixText }}</span>
                }
              </ng-template>
            </data-table>
          }
        </soft-card>
      } @else {
        <soft-card variant="bordered"><div class="soft-empty" role="status">{{ noControlledDay }}.</div></soft-card>
      }
     </div>
    }
  `,
  styles: [
    `
      /* « relative » : les légendes masquées (.closing-sr, position absolute) restent dans la zone de défilement de la page. */
      :host { display: flex; flex-direction: column; gap: 16px; position: relative; }
      /* Les blocs partagés portent leurs propres marges basses : l'espacement de la page vient du « gap » de l'hôte. */
      :host ::ng-deep .soft-page-header { margin-bottom: 0; }
      :host ::ng-deep .soft-toolbar { margin-bottom: 0; }
      .closing-banner {
        margin: 0; padding: 10px 14px; font-size: 13px;
        border-radius: var(--radius-md); border: 1px solid var(--info-border);
        background: var(--info-bg); color: var(--info);
      }
      .closing-results { display: flex; flex-direction: column; gap: 16px; }
      .closing-results[aria-busy='true'] { opacity: 0.6; transition: opacity 0.15s; }
      .closing-status-line { min-height: 20px; font-size: 13px; }
      .closing-stale { color: var(--warning, var(--text-secondary)); font-weight: 600; }
      .closing-busy { display: inline-flex; align-items: center; gap: 8px; color: var(--text-secondary); font-weight: 600; }
      .closing-busy__dot {
        width: 12px; height: 12px; border-radius: 50%;
        border: 2px solid var(--border-color, var(--text-muted)); border-top-color: var(--accent);
        animation: closing-spin 0.8s linear infinite;
      }
      @keyframes closing-spin { to { transform: rotate(360deg); } }
      @media (prefers-reduced-motion: reduce) { .closing-busy__dot { animation: none; } }
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
  private readonly state = inject(ClosingSessionState);

  /** Filtre partagé, positionné sur le mois précédent avant son premier rendu. */
  @ViewChild(PeriodFilter, { static: true }) private filter?: PeriodFilter;

  /** Colonnes du tableau partagé : la gravité et la correction passent par des gabarits de cellule. */
  readonly columns: Column[] = [
    { key: 'gravite', label: 'Gravité', format: (v) => severityLabel(String(v)) },
    { key: 'type', label: 'Type', format: (v) => typeLabel(String(v)) },
    { key: 'matricule', label: 'Matricule' },
    { key: 'salarie', label: 'Salarié' },
    { key: 'departement', label: 'Département', format: (v) => (v ? String(v) : '—') },
    { key: 'date', label: 'Date', format: (v) => formatDay(String(v)) },
    { key: 'detail', label: 'Détail' },
    { key: 'correction', label: 'Correction', format: () => '' },
  ];
  readonly severities = SEVERITIES;
  readonly purposeNote = PURPOSE_NOTE;
  readonly disabledMessage = DISABLED_MESSAGE;
  readonly noAnomaly = NO_ANOMALY;
  readonly informationRule = INFORMATION_RULE;
  readonly nothingToFixText = NOTHING_TO_FIX;
  readonly noControlledDay = NO_CONTROLLED_DAY;
  readonly analysingText = ANALYSING_TEXT;
  readonly staleText = FILTER_CHANGED_TEXT;
  readonly notAnalysedText = NOT_ANALYSED_TEXT;

  readonly report = signal<ClosingReport | null>(null);
  /** Analyse complète en cours (bouton Analyser, onglet) : indicateur près du filtre, résultat atténué. */
  readonly analysing = signal(false);
  /** Changement de page ou de taille seulement : seul le tableau affiche un chargement. */
  readonly paging = signal(false);
  readonly error = signal<string | null>(null);
  readonly disabled = signal(false);
  readonly includeComparison = signal(false);
  /** Onglet de la liste : « afix » (par défaut) ou « all » (avec les informations, includeInformation = true). */
  readonly tab = signal<'afix' | 'all'>('afix');
  readonly includeInformation = computed(() => this.tab() === 'all');
  readonly page = signal(1);
  readonly pageSize = signal<number>(DEFAULT_PAGE_SIZE);

  /** Filtre qui a produit le résultat affiché (null : aucune analyse faite). */
  private readonly applied = signal<ClosingFilter | null>(null);
  private readonly analysedAt = signal<Date | null>(null);
  /** Filtre actuellement saisi dans le formulaire partagé. */
  private readonly draft = signal<ClosingFilter>({ ...defaultPeriod(), departement: null, includeComparison: false });
  /** Filtre de la requête en vol, pour ignorer un second clic identique. */
  private inflight: ClosingFilter | null = null;
  private request?: Subscription;

  /** La saisie diffère du résultat affiché : rien n'est relancé, l'utilisateur doit cliquer sur Analyser. */
  readonly stale = computed(() => this.applied() !== null && !sameFilter(this.draft(), this.applied()));
  readonly banner = computed(() => {
    const at = this.analysedAt();
    const filter = this.applied();
    return at && filter ? resultBannerText(at, filter) : null;
  });

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
  /** Lignes de la page courante pour le tableau partagé (le nom affiché suit le masquage serveur). */
  readonly rows = computed<Record<string, unknown>[]>(() =>
    (this.report()?.anomalies.items ?? []).map((a) => ({ ...a, salarie: personLabel(a) })),
  );
  readonly anomaliesCaption = computed(() => {
    const p = this.report()?.anomalies;
    return p ? `Anomalies de la période, page ${p.page} sur ${p.totalPages}` : null;
  });

  ngOnInit(): void {
    // Retour sur la page : on réaffiche l'état conservé (filtre saisi, dernier résultat), sans rien relancer.
    this.state.syncContext();
    const draft = this.state.draft() ?? { ...defaultPeriod(), departement: null, includeComparison: false };
    if (this.filter) {
      this.filter.from = draft.from;
      this.filter.to = draft.to;
      this.filter.departement = draft.departement;
    }
    this.includeComparison.set(draft.includeComparison);
    this.draft.set(draft);
    const kept = this.state.result();
    if (kept) {
      this.report.set(kept.report);
      this.applied.set(kept.filter);
      this.analysedAt.set(kept.analysedAt);
      this.tab.set(this.state.tab());
      this.page.set(kept.report.anomalies.page);
      this.pageSize.set(this.state.pageSize() ?? DEFAULT_PAGE_SIZE);
    }
  }

  ngOnDestroy(): void {
    // Le filtre saisi est conservé ; une requête en cours n'est pas annulée : son résultat sera gardé en mémoire.
    this.state.draft.set(this.readFilter());
  }

  /** Le filtre a été modifié par l'utilisateur : relit ses champs pour savoir si la saisie diffère du résultat affiché. */
  refreshDraft(): void {
    this.draft.set(this.readFilter());
  }

  private readFilter(): ClosingFilter {
    return {
      from: this.filter?.from ?? this.draft().from,
      to: this.filter?.to ?? this.draft().to,
      departement: this.filter?.departement || null,
      includeComparison: this.includeComparison(),
    };
  }

  /** Bouton « Analyser » : clics répétés sur le même filtre ignorés ; un autre filtre annule la requête précédente. */
  onSearch(req: PeriodRequest): void {
    const next: ClosingFilter = {
      from: req.from,
      to: req.to,
      departement: req.departement || null,
      includeComparison: this.includeComparison(),
    };
    if (this.analysing() && sameFilter(next, this.inflight)) return;
    this.page.set(1);
    this.load(next, false);
  }

  /** L'option est mémorisée avec le filtre : rien n'est relancé avant le clic sur Analyser. */
  onComparison(active: boolean): void {
    this.includeComparison.set(!!active);
    this.draft.set(this.readFilter());
  }

  /** Changer d'onglet relance l'analyse du filtre déjà analysé, à la page 1 (le filtre saisi n'est pas pris en compte). */
  onTab(id: string): void {
    const next = id === 'all' ? 'all' : 'afix';
    const applied = this.applied();
    if (next === this.tab() || !applied) return;
    this.tab.set(next);
    this.state.tab.set(next);
    this.page.set(1);
    this.load(applied, false);
  }

  /** Navigation du tableau partagé : un changement de taille revient à la page 1. */
  onPageChange(e: PageChangeEvent): void {
    const applied = this.applied();
    if (!applied) return;
    if (e.pageSize !== this.pageSize()) {
      this.pageSize.set(e.pageSize || DEFAULT_PAGE_SIZE);
      this.page.set(1);
      this.load(applied, true);
      return;
    }
    this.goTo(e.page);
  }

  goTo(page: number): void {
    const applied = this.applied();
    if (!applied || page < 1 || page === this.page()) return;
    this.page.set(page);
    this.load(applied, true);
  }

  /**
   * Lance la requête pour un filtre donné. `pageOnly` : changement de page ou de taille seulement, le
   * serveur peut alors réutiliser l'analyse déjà calculée (60 s) et seul le tableau montre un chargement ;
   * toute nouvelle analyse recalcule et montre l'indicateur « Analyse en cours… ».
   * L'intercepteur affiche déjà l'erreur en notification : la page ne montre qu'un état, sans second message,
   * et garde le dernier bon résultat.
   */
  private load(filter: ClosingFilter, pageOnly: boolean): void {
    this.request?.unsubscribe();
    this.inflight = filter;
    this.analysing.set(!pageOnly);
    this.paging.set(pageOnly);
    this.error.set(null);
    this.disabled.set(false);
    const context = this.state.currentContext();
    this.request = this.closing
      .analysis({
        from: filter.from,
        to: filter.to,
        departement: filter.departement,
        includeComparison: filter.includeComparison,
        includeInformation: this.includeInformation(),
        page: this.page(),
        pageSize: this.pageSize(),
        reuseCache: pageOnly,
      })
      .subscribe({
        next: (r) => {
          const at = pageOnly ? (this.analysedAt() ?? new Date()) : new Date();
          this.report.set(r);
          this.page.set(r.anomalies.page);
          this.applied.set(filter);
          this.analysedAt.set(at);
          this.state.tab.set(this.tab());
          this.state.page.set(r.anomalies.page);
          this.state.pageSize.set(this.pageSize());
          this.state.storeResult({ filter, report: r, analysedAt: at }, context);
          this.endRequest();
        },
        error: (err: HttpErrorResponse) => {
          this.endRequest();
          this.page.set(this.report()?.anomalies.page ?? 1);
          if (isDisabledError(err)) this.disabled.set(true);
          else this.error.set(errorMessage(err));
        },
      });
  }

  private endRequest(): void {
    this.inflight = null;
    this.analysing.set(false);
    this.paging.set(false);
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

  /** Les lignes du tableau partagé sont les anomalies elles-mêmes (plus le nom affiché). */
  view(row: Record<string, unknown>): ClosingAnomalyView {
    return row as unknown as ClosingAnomalyView;
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
