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
import { RouterLink } from '@angular/router';
import { Subscription } from 'rxjs';
import { errorMessage } from '../../core/error.logic';
import { SessionStore } from '../../core/session.store';
import { PageHeader, PeriodFilter, SoftBadge, SoftCard } from '../../shared/components';
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
  SHOW_INFORMATION_LABEL,
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
  imports: [PageHeader, PeriodFilter, SoftCard, SoftBadge, RouterLink],
  template: `
    <page-header title="Clôture de paie" subtitle="Contrôle des données de la période avant la clôture"></page-header>

    <p class="closing-note" role="note">{{ purposeNote }}</p>

    <soft-card padding="sm">
      <div class="closing-filters" role="search" aria-label="Période et département analysés">
        <period-filter
          [showMatricule]="false"
          [showDepartement]="true"
          searchLabel="Analyser"
          (search)="onSearch($event)"
        ></period-filter>
        <label class="closing-check">
          <input type="checkbox" [checked]="includeComparison()" (change)="onComparison($event)" />
          Comparer avec la période précédente
        </label>
        <label class="closing-check">
          <input type="checkbox" [checked]="includeInformation()" (change)="onInformation($event)" />
          {{ showInformationLabel }}
        </label>
      </div>
    </soft-card>

    @if (loading() && !report()) {
      <div class="soft-empty" role="status">Analyse de la période en cours…</div>
    } @else if (disabled()) {
      <div class="closing-state closing-state--info" role="status">{{ disabledMessage }}</div>
    } @else if (error()) {
      <div class="closing-state closing-state--error" role="alert">{{ error() }}</div>
    } @else if (report(); as r) {
      <section class="closing-overview" aria-label="Synthèse de la période">
        <soft-card>
          <div class="closing-status">
            <span class="closing-status__label">Statut</span>
            <soft-badge [variant]="status().tone">{{ status().label }}</soft-badge>
            <span class="closing-status__hint">{{ status().hint }}</span>
          </div>
          <div class="closing-score">
            <span class="closing-status__label">Score</span>
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

        <div class="closing-counts">
          @for (s of severities; track s) {
            <soft-card variant="bordered" padding="sm">
              <div class="closing-count">
                <soft-badge [variant]="tone(s)">{{ severityText(s) }}</soft-badge>
                <strong>{{ count(r, s) }}</strong>
                <span>{{ sentence(s, count(r, s)) }}</span>
              </div>
            </soft-card>
          }
        </div>
      </section>

      <soft-card padding="sm">
        <h2 class="closing-h2">Résumé</h2>
        <ul class="closing-summary">
          @for (p of summary(); track $index) {
            <li>{{ p }}</li>
          }
        </ul>
      </soft-card>

      @if (r.joursEmployeControles > 0) {
        <soft-card padding="sm">
          <h2 class="closing-h2">Par département</h2>
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

        <soft-card padding="sm">
          <h2 class="closing-h2">Anomalies à corriger</h2>
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
                            <a class="closing-fix" [routerLink]="l.path" [queryParams]="l.queryParams" [attr.aria-label]="l.ariaLabel">
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
        <div class="soft-empty" role="status">{{ noControlledDay }}.</div>
      }
    }
  `,
  styles: [
    `
      :host { display: flex; flex-direction: column; gap: 16px; }
      .closing-note { margin: 0; color: var(--text-secondary); font-size: 0.9rem; }
      .closing-filters { display: flex; flex-wrap: wrap; align-items: flex-end; gap: 12px 20px; }
      .closing-check { display: inline-flex; align-items: center; gap: 8px; font-size: 0.9rem; color: var(--app-text); }
      .closing-check input:focus-visible,
      .closing-fix:focus-visible,
      .closing-explain summary:focus-visible {
        outline: 2px solid var(--focus-ring);
        outline-offset: 2px;
      }
      .closing-state { padding: 14px 16px; border-radius: var(--radius-lg); border: 1px solid; }
      .closing-state--error { color: var(--danger); background: var(--danger-bg); border-color: var(--danger-border); }
      .closing-state--success { color: var(--success); background: var(--success-bg); border-color: var(--success-border); }
      .closing-state--info { color: var(--app-text); background: var(--app-surface-soft); border-color: var(--color-border); }
      .closing-overview { display: grid; grid-template-columns: minmax(0, 2fr) minmax(0, 3fr); gap: 16px; }
      @media (max-width: 900px) { .closing-overview { grid-template-columns: minmax(0, 1fr); } }
      .closing-status, .closing-score { display: flex; flex-wrap: wrap; align-items: center; gap: 10px; margin-bottom: 8px; }
      .closing-status__label { color: var(--text-muted); font-size: 0.85rem; min-width: 52px; }
      .closing-status__hint { color: var(--text-secondary); font-size: 0.9rem; }
      .closing-score__value { font-size: 1.6rem; color: var(--app-text-strong); }
      .closing-period { margin: 4px 0 8px; color: var(--text-secondary); font-size: 0.9rem; }
      .closing-excluded { color: var(--app-text); }
      .closing-explain summary { cursor: pointer; color: var(--accent); font-size: 0.9rem; }
      .closing-explain p { margin: 8px 0 0; font-size: 0.9rem; color: var(--app-text); }
      .closing-counts { display: grid; grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); gap: 12px; }
      .closing-count { display: flex; flex-direction: column; gap: 6px; }
      .closing-count strong { font-size: 1.5rem; color: var(--app-text-strong); }
      .closing-count span { color: var(--text-secondary); font-size: 0.85rem; }
      .closing-h2 { margin: 0 0 12px; font-size: 1rem; font-weight: 600; color: var(--app-text-strong); }
      .closing-summary { margin: 0; padding-left: 20px; display: grid; gap: 4px; color: var(--app-text); }
      .closing-muted { color: var(--text-muted); font-size: 0.85rem; }
      .closing-fix { color: var(--accent); font-weight: 600; white-space: nowrap; }
      .closing-fix:hover { color: var(--accent-hover); }
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
  readonly showInformationLabel = SHOW_INFORMATION_LABEL;
  readonly noControlledDay = NO_CONTROLLED_DAY;

  readonly report = signal<ClosingReport | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly disabled = signal(false);
  readonly includeComparison = signal(false);
  readonly includeInformation = signal(false);
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
  readonly excluded = computed(() => excludedEmployeesText(this.report()?.salariesSansPlanningExclus));
  /** Liste vide parce que seules des informations existent (masquées) : message calme, jamais « aucune anomalie ». */
  readonly hiddenInformationText = computed(() => {
    const n = countOf(this.report()?.parGravite, 'Information');
    return `Aucune anomalie à corriger. ${n} ${n > 1 ? 'informations masquées' : 'information masquée'} : cochez « ${SHOW_INFORMATION_LABEL} » pour ${n > 1 ? 'les' : 'la'} voir.`;
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

  onComparison(event: Event): void {
    this.includeComparison.set((event.target as HTMLInputElement).checked);
    this.load();
  }

  onInformation(event: Event): void {
    this.includeInformation.set((event.target as HTMLInputElement).checked);
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
