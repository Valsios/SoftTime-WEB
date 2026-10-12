import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DashboardStats, PeriodRequest } from '../../shared/models';
import { DashboardService } from '../../shared/services/dashboard.service';
import {
  Column,
  DataTable,
  PageHeader,
  PeriodFilter,
  SoftCard,
  SoftSelect,
} from '../../shared/components';
import { asRow } from '../../shared/utils/date';

/**
 * Analyse de la période : indicateurs clés, absences et retards.
 * Page distincte de l'accueil (`/dashboard`), qui reste le tableau de bord de session.
 *
 * Les absences « planifiées sans pointage » n'excluent pas encore les congés SAGE (LOT 9) :
 * le libellé le dit explicitement pour ne pas être lu comme une absence avérée.
 */
@Component({
  selector: 'app-analytics',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftCard, SoftSelect, DataTable, PeriodFilter],
  template: `
    <page-header title="Analyse de la période" subtitle="Indicateurs, absences et retards"></page-header>

    <div class="soft-toolbar">
      <soft-select
        label="Regroupement"
        [(ngModel)]="periode"
        name="periode"
        [options]="periodeOptions"
        (ngModelChange)="reload()"
      ></soft-select>
    </div>
    <period-filter [autoSearch]="true" (search)="onSearch($event)"></period-filter>

    @if (stats(); as s) {
      <div class="kpi-grid">
        <soft-card padding="md">
          <span class="kpi-label">Employés</span><strong>{{ s.totalEmployes }}</strong>
          <small>{{ s.kpi.cartesActives }} carte(s) active(s)</small>
        </soft-card>
        <soft-card padding="md">
          <span class="kpi-label">Absences</span><strong>{{ s.absences.total }}</strong>
          <small>{{ s.absences.explicites }} explicite(s) · {{ s.absences.planifiesSansPointage }} jour(s) planifié(s) sans pointage</small>
        </soft-card>
        <soft-card padding="md">
          <span class="kpi-label">Retards</span><strong>{{ s.retards.total }}</strong>
          <small>{{ minutes(s.retards.totalMinutes) }} cumulées</small>
        </soft-card>
        <soft-card padding="md">
          <span class="kpi-label">Anomalies</span><strong>{{ s.kpi.anomalies }}</strong>
          <small>{{ s.kpi.correctionsManuelles }} correction(s) manuelle(s)</small>
        </soft-card>
        <soft-card padding="md">
          <span class="kpi-label">Semaines validées</span><strong>{{ s.kpi.semainesValidees }}</strong>
          <small>{{ s.kpi.semainesEnAttente }} en attente de validation</small>
        </soft-card>
        <soft-card padding="md">
          <span class="kpi-label">Heures</span><strong>{{ fmtHeures(s.kpi.heuresTravaillees) }}</strong>
          <small>{{ fmtHeures(s.kpi.heuresSupplementaires) }} h supplémentaires</small>
        </soft-card>
      </div>

      <h2 class="section-title">Absences par période</h2>
      <soft-card padding="md">
        <data-table [columns]="groupColumns" [rows]="absenceRows()" [loading]="loading()"></data-table>
      </soft-card>

      <h2 class="section-title">Retards par période</h2>
      <soft-card padding="md">
        <data-table [columns]="groupColumns" [rows]="retardRows()" [loading]="loading()"></data-table>
      </soft-card>

      <h2 class="section-title">Top 5 absences</h2>
      <soft-card padding="md">
        <data-table [columns]="topColumns" [rows]="topAbsenceRows()" [loading]="loading()"></data-table>
      </soft-card>

      <h2 class="section-title">Top 5 retards</h2>
      <soft-card padding="md">
        <data-table [columns]="topColumns" [rows]="topRetardRows()" [loading]="loading()"></data-table>
      </soft-card>
    } @else {
      <soft-card padding="md">
        <p class="muted">Sélectionnez une période pour afficher l'analyse.</p>
      </soft-card>
    }
  `,
  styles: [
    `
      .kpi-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(190px, 1fr));
        gap: 14px;
        margin-bottom: 28px;
      }
      .kpi-grid strong { display: block; font-size: 26px; line-height: 1.2; }
      .kpi-grid small { color: var(--text-muted); font-size: 12px; }
      .kpi-label {
        display: block;
        font-size: 12px;
        text-transform: uppercase;
        letter-spacing: 0.04em;
        color: var(--text-muted);
      }
      .section-title { font-size: 18px; margin: 0 0 14px; }
      .muted { color: var(--text-muted); }
    `,
  ],
})
export class AnalyticsPage {
  private readonly svc = inject(DashboardService);

  readonly loading = signal(false);
  readonly stats = signal<DashboardStats | null>(null);
  readonly periodeOptions = [
    { value: 'quotidienne', label: 'Quotidienne' },
    { value: 'hebdomadaire', label: 'Hebdomadaire' },
    { value: 'mensuelle', label: 'Mensuelle' },
  ];
  periode = 'mensuelle';

  readonly groupColumns: Column[] = [
    { key: 'periode', label: 'Période' },
    { key: 'count', label: 'Nombre' },
    { key: 'totalMinutes', label: 'Minutes' },
  ];

  readonly topColumns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'nom', label: 'Nom' },
    { key: 'prenom', label: 'Prénom' },
    { key: 'departement', label: 'Département' },
    { key: 'count', label: 'Nombre' },
    { key: 'totalMinutes', label: 'Minutes' },
  ];

  readonly absenceRows = computed(() => (this.stats()?.absences.stats ?? []).map(asRow));
  readonly retardRows = computed(() => (this.stats()?.retards.stats ?? []).map(asRow));
  readonly topAbsenceRows = computed(() => (this.stats()?.absences.top ?? []).map(asRow));
  readonly topRetardRows = computed(() => (this.stats()?.retards.top ?? []).map(asRow));

  private lastFilter: PeriodRequest | null = null;

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    this.load();
  }

  reload(): void {
    this.load();
  }

  minutes(total: number | null | undefined): string {
    const value = total ?? 0;
    const heures = Math.floor(value / 60);
    const minutes = value % 60;
    return heures > 0 ? `${heures} h ${minutes.toString().padStart(2, '0')}` : `${minutes} min`;
  }

  fmtHeures(value: number | null | undefined): string {
    return (value ?? 0).toFixed(2);
  }

  private load(): void {
    if (!this.lastFilter) return;
    this.loading.set(true);
    this.svc.stats({ ...this.lastFilter, periode: this.periode }).subscribe({
      next: (s) => {
        this.stats.set(s);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }
}
