import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DashboardFilters, DashboardStats, PeriodRequest } from '../../shared/models';
import { DashboardService } from '../../shared/services/dashboard.service';
import {
  ChartPoint,
  ChartSeries,
  Column,
  DataTable,
  PageHeader,
  PeriodFilter,
  SoftBarChart,
  SoftButton,
  SoftCard,
  SoftDonutChart,
  SoftLineChart,
  SoftSelect,
  SoftTabs,
  TabItem,
} from '../../shared/components';
import { asRow, fmtDate } from '../../shared/utils/date';

/**
 * Libellé de période réduit pour l'axe des courbes : « Semaine 27 (30/06 - 06/07/2025) »
 * devient « S27 ». Les tableaux, eux, gardent le libellé complet (c'est là qu'on le lit).
 */
function libelleCourt(periode: string): string {
  const semaine = /^Semaine\s+(\d+)/.exec(periode);
  return semaine ? `S${semaine[1]}` : fmtDate(periode);
}

/**
 * Analyse de la période : KPI, évolution, répartition et assiduité.
 * Page distincte de l'accueil (`/dashboard`), qui reste le tableau de bord de session.
 *
 * Les absences « planifiées sans pointage » n'excluent pas encore les congés SAGE (LOT 9) :
 * le libellé le dit explicitement pour ne pas être lu comme une absence avérée.
 * Les taux sont calculés côté serveur (`DashboardRules`) pour rester cohérents avec la paie.
 */
@Component({
  selector: 'app-analytics',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    PageHeader,
    SoftCard,
    SoftSelect,
    SoftButton,
    SoftTabs,
    DataTable,
    PeriodFilter,
    SoftLineChart,
    SoftBarChart,
    SoftDonutChart,
  ],
  template: `
    <page-header
      title="Analyse de la période"
      subtitle="Indicateurs, évolution, répartition et assiduité"
    ></page-header>

    <div class="soft-toolbar">
      <soft-select
        label="Regroupement"
        [(ngModel)]="periode"
        name="periode"
        [options]="periodeOptions"
        (ngModelChange)="reload()"
      ></soft-select>
      <soft-select
        label="Département"
        placeholder="Tous"
        [(ngModel)]="departement"
        name="departement"
        [options]="departementOptions()"
        (ngModelChange)="reload()"
      ></soft-select>
      <soft-select
        label="Catégorie"
        placeholder="Toutes"
        [(ngModel)]="categorie"
        name="categorie"
        [options]="categorieOptions()"
        (ngModelChange)="reload()"
      ></soft-select>
    </div>
    <period-filter [autoSearch]="true" (search)="onSearch($event)"></period-filter>

    @if (stats(); as s) {
      <div class="kpi-grid">
        <soft-card padding="md">
          <span class="kpi-label">Effectif actif</span><strong>{{ s.kpi.cartesActives }}</strong>
          <small>{{ s.totalEmployes }} carte(s) sur la période</small>
        </soft-card>
        <soft-card padding="md">
          <span class="kpi-label">Taux d'absentéisme</span><strong>{{ s.kpi.tauxAbsenteisme }} %</strong>
          <small>{{ s.kpi.joursAbsence }} j sur {{ s.kpi.joursPlanifies }} j planifiés</small>
        </soft-card>
        <soft-card padding="md">
          <span class="kpi-label">Taux de ponctualité</span><strong>{{ s.kpi.tauxPonctualite }} %</strong>
          <small>{{ s.kpi.joursRetard }} j en retard sur {{ s.kpi.joursTravailles }} j pointés</small>
        </soft-card>
        <soft-card padding="md">
          <span class="kpi-label">Heures supplémentaires</span>
          <strong>{{ fmtHeures(s.kpi.heuresSupplementaires) }}</strong>
          <small>{{ fmtHeures(s.kpi.heuresTravaillees) }} h travaillées</small>
        </soft-card>
        <soft-card padding="md">
          <span class="kpi-label">Semaines validées</span><strong>{{ s.kpi.semainesValidees }}</strong>
          <small>{{ s.kpi.semainesEnAttente }} en attente de validation</small>
        </soft-card>
      </div>

      <p class="kpi-detail">
        <strong>{{ s.absences.total }}</strong> absence(s) — {{ s.absences.explicites }} explicite(s),
        {{ s.absences.planifiesSansPointage }} planifiée(s) sans pointage ·
        <strong>{{ s.retards.total }}</strong> retard(s) — {{ minutes(s.retards.totalMinutes) }} cumulées ·
        <strong>{{ s.kpi.anomalies }}</strong> anomalie(s) ·
        {{ s.kpi.correctionsManuelles }} correction(s) manuelle(s)
      </p>

      <div class="chart-grid">
        <div>
          <h2 class="section-title">Évolution des absences et des retards</h2>
          <soft-card padding="md">
            <soft-line-chart
              [labels]="periodeLabels()"
              [series]="activiteSeries()"
              [format]="formatCount"
            ></soft-line-chart>
          </soft-card>
        </div>
        <div>
          <h2 class="section-title">Évolution des heures</h2>
          <soft-card padding="md">
            <soft-line-chart
              [labels]="periodeLabels()"
              [series]="heuresSeries()"
              [format]="formatHeures"
            ></soft-line-chart>
          </soft-card>
        </div>
      </div>

      <h2 class="section-title">Répartition de l'effectif actif</h2>
      <soft-card padding="md">
        <soft-tabs [tabs]="axes" [(activeTab)]="axe"></soft-tabs>
        @if (axe() === 'branche') {
          <soft-donut-chart [items]="brancheItems()" [format]="formatCount"></soft-donut-chart>
        } @else if (axe() === 'departement') {
          <soft-bar-chart
            [items]="departementBars()"
            color="var(--accent-600)"
            [format]="formatCount"
          ></soft-bar-chart>
        } @else {
          <soft-bar-chart [items]="categorieBars()" [format]="formatCount"></soft-bar-chart>
        }

        <h3 class="sub-title">Activité par {{ titreAxe() }}</h3>
        <data-table
          [columns]="repartitionColumns()"
          [rows]="repartitionRows()"
          [loading]="loading()"
        ></data-table>
      </soft-card>

      <h2 class="section-title">Absences et retards par période</h2>
      <soft-card padding="md">
        <data-table
          [columns]="periodeColumns"
          [rows]="periodeRows()"
          [loading]="loading()"
        ></data-table>
      </soft-card>

      <h2 class="section-title">Assiduité — les plus ponctuels</h2>
      <soft-card padding="md">
        <data-table
          [columns]="assiduiteColumns"
          [rows]="plusPonctuelsRows()"
          [loading]="loading()"
          emptyText="Aucun pointage sur la période."
        ></data-table>
      </soft-card>

      <h2 class="section-title">Assiduité — les moins ponctuels</h2>
      <soft-card padding="md">
        <data-table
          [columns]="assiduiteColumns"
          [rows]="moinsPonctuelsRows()"
          [loading]="loading()"
          emptyText="Aucun pointage sur la période."
        ></data-table>
      </soft-card>
    } @else if (erreur()) {
      <soft-card padding="md">
        <p class="muted">L'analyse n'a pas pu être chargée (délai ou serveur indisponible).</p>
        <soft-button (click)="reload()">Réessayer</soft-button>
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
        margin-bottom: 10px;
      }
      .kpi-detail {
        margin: 0 0 28px;
        color: var(--text-secondary);
        font-size: 13px;
      }
      .kpi-detail strong { color: var(--app-text); }
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
      .chart-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
        gap: 16px;
        margin-bottom: 28px;
      }
      .chart-grid .section-title { margin-top: 0; }
      .sub-title {
        margin: 20px 0 10px;
        font-size: 14px;
        color: var(--text-secondary);
      }
      .muted { color: var(--text-muted); }
    `,
  ],
})
export class AnalyticsPage implements OnInit {
  private readonly svc = inject(DashboardService);

  readonly loading = signal(false);
  readonly erreur = signal(false);
  readonly stats = signal<DashboardStats | null>(null);
  readonly filters = signal<DashboardFilters | null>(null);

  readonly periodeOptions = [
    { value: 'quotidienne', label: 'Quotidienne' },
    { value: 'hebdomadaire', label: 'Hebdomadaire' },
    { value: 'mensuelle', label: 'Mensuelle' },
  ];
  periode = 'mensuelle';
  departement: string | null = null;
  categorie: number | null = null;

  readonly departementOptions = computed(() =>
    (this.filters()?.departements ?? []).map((value) => ({ value, label: value })),
  );
  readonly categorieOptions = computed(() =>
    (this.filters()?.categories ?? []).map((c) => ({ value: c.id, label: c.libelle })),
  );

  /** Un onglet = une ventilation de l'effectif, et le tableau d'activité suit l'onglet actif. */
  readonly axes: TabItem[] = [
    { id: 'categorie', label: 'Catégorie' },
    { id: 'branche', label: 'Branche' },
    { id: 'departement', label: 'Département' },
  ];
  readonly axe = signal('categorie');

  readonly titreAxe = computed(() =>
    this.axe() === 'branche' ? 'Branche' : this.axe() === 'departement' ? 'Département' : 'Catégorie',
  );

  readonly periodeColumns: Column[] = [
    { key: 'periode', label: 'Période' },
    { key: 'absences', label: 'Absences', align: 'right' },
    { key: 'retards', label: 'Retards', align: 'right' },
    { key: 'minutes', label: 'Minutes retard', align: 'right' },
  ];

  readonly repartitionColumns = computed<Column[]>(() => [
    { key: 'libelle', label: this.titreAxe() },
    { key: 'effectif', label: 'Effectif actif', align: 'right' },
    { key: 'absences', label: 'Absences', align: 'right' },
    { key: 'retards', label: 'Retards', align: 'right' },
    { key: 'minutesRetard', label: 'Minutes retard', align: 'right' },
    { key: 'heuresSupplementaires', label: 'HS', align: 'right', format: (value) => Number(value ?? 0).toFixed(2) },
  ]);

  readonly assiduiteColumns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'nom', label: 'Nom' },
    { key: 'tauxPonctualite', label: 'Ponctualité', align: 'right', format: (value) => `${Number(value ?? 0)} %` },
    { key: 'joursTravailles', label: 'Jours pointés', align: 'right' },
    { key: 'joursRetard', label: 'Retards', align: 'right' },
    { key: 'joursAbsence', label: 'Absences', align: 'right' },
  ];

  readonly formatCount = (value: number): string => String(value ?? 0);
  readonly formatHeures = (value: number): string => (value ?? 0).toFixed(2);

  readonly periodeLabels = computed(() =>
    (this.stats()?.absences.stats ?? []).map((g) => libelleCourt(g.periode)),
  );

  readonly activiteSeries = computed<ChartSeries[]>(() => {
    const stats = this.stats();
    if (!stats) return [];
    return [
      { name: 'Absences', color: 'var(--danger)', values: stats.absences.stats.map((g) => g.count) },
      { name: 'Retards', color: 'var(--warning)', values: stats.retards.stats.map((g) => g.count) },
    ];
  });

  readonly heuresSeries = computed<ChartSeries[]>(() => {
    const stats = this.stats();
    if (!stats) return [];
    return [
      { name: 'Heures travaillées', color: 'var(--primary-500)', values: stats.series.map((s) => s.heures) },
      {
        name: 'Heures supplémentaires',
        color: 'var(--accent-600)',
        values: stats.series.map((s) => s.heuresSupplementaires),
      },
    ];
  });

  readonly categorieBars = computed<ChartPoint[]>(() =>
    (this.stats()?.repartition.parCategorie ?? []).map((s) => ({ label: s.libelle, value: s.effectif })),
  );
  readonly brancheItems = computed<ChartPoint[]>(() =>
    (this.stats()?.repartition.parBranche ?? []).map((s) => ({ label: s.libelle, value: s.effectif })),
  );
  readonly departementBars = computed<ChartPoint[]>(() =>
    (this.stats()?.repartition.parDepartement ?? []).map((s) => ({ label: s.libelle, value: s.effectif })),
  );

  readonly repartitionRows = computed(() => {
    const repartition = this.stats()?.repartition;
    if (!repartition) return [];
    const slices =
      this.axe() === 'branche'
        ? repartition.parBranche
        : this.axe() === 'departement'
          ? repartition.parDepartement
          : repartition.parCategorie;
    return slices.map(asRow);
  });

  /**
   * Tableau fusionné des deux courbes du haut. Les deux listes viennent du même découpage serveur
   * (`PeriodGrouping.Clefs`), on les joint par libellé pour ne pas dépendre de l'ordre.
   */
  readonly periodeRows = computed(() => {
    const stats = this.stats();
    if (!stats) return [];
    const retards = new Map(stats.retards.stats.map((r) => [r.periode, r]));
    return stats.absences.stats.map((a) => {
      const retard = retards.get(a.periode);
      return {
        periode: a.periode,
        absences: a.count,
        retards: retard?.count ?? 0,
        minutes: retard?.totalMinutes ?? 0,
      };
    });
  });

  readonly plusPonctuelsRows = computed(() => (this.stats()?.assiduite.plusPonctuels ?? []).map(asRow));
  readonly moinsPonctuelsRows = computed(() => (this.stats()?.assiduite.moinsPonctuels ?? []).map(asRow));

  private lastFilter: PeriodRequest | null = null;

  ngOnInit(): void {
    // Les listes de filtres ne dépendent pas de la période : une seule charge au montage.
    this.svc.filters().subscribe({
      next: (filters) => this.filters.set(filters),
      error: () => this.filters.set(null),
    });
  }

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
    this.erreur.set(false);
    this.svc
      .stats({
        ...this.lastFilter,
        periode: this.periode,
        departement: this.departement,
        categorie: this.categorie,
      })
      .subscribe({
        next: (s) => {
          this.stats.set(s);
          this.loading.set(false);
        },
        error: () => {
          this.erreur.set(true);
          this.loading.set(false);
        },
      });
  }
}
