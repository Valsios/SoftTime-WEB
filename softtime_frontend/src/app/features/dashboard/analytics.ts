import {
  ChangeDetectionStrategy,
  ChangeDetectorRef,
  Component,
  computed,
  inject,
  OnInit,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import {
  DashboardAssiduiteRow,
  DashboardFilters,
  DashboardSlice,
  DashboardStats,
  PeriodRequest,
} from '../../shared/models';
import { DashboardService } from '../../shared/services/dashboard.service';
import { ExcelExportService, type ExcelSheet } from '../../shared/services/excel-export.service';
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
  SoftLineChart,
  SoftSelect,
  SoftTabs,
  TabItem,
} from '../../shared/components';
import { asRow, fmtDate, parseLocalDate } from '../../shared/utils/date';

/**
 * Libellé de période réduit pour l'axe des courbes : « Semaine 27 (30/06 - 06/07/2025) »
 * devient « S27 ». Les tableaux, eux, gardent le libellé complet (c'est là qu'on le lit).
 */
function libelleCourt(periode: string): string {
  const semaine = /^Semaine\s+(\d+)/.exec(periode);
  return semaine ? `S${semaine[1]}` : fmtDate(periode);
}

/**
 * Filtres réellement appliqués à une réponse du serveur : sert au contexte affiché et aux
 * exports, pour qu'ils ne puissent pas décrire des filtres différents des données visibles.
 */
interface ReportContext {
  from: string;
  to: string;
  matriculeFrom: string | null;
  matriculeTo: string | null;
  periode: string;
  departement: string | null;
  categorie: number | null;
}

/**
 * Analyse de la période : KPI, évolution, répartition et assiduité.
 * Page distincte de l'accueil (`/dashboard`), qui reste le tableau de bord de session.
 *
 * Les absences « planifiées sans pointage » excluent désormais les congés SAGE (LOT 9) : un jour de
 * congé n'est ni une absence ni une présence théorique, exactement comme dans la validation semaine.
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
  ],
  template: `
    <page-header
      title="Analyse de la période"
      subtitle="Indicateurs, évolution, répartition et assiduité"
    >
      <soft-button
        variant="secondary"
        (click)="exportExcel()"
        [disabled]="!stats() || loading()"
      >Export Excel</soft-button>
      <soft-button
        variant="secondary"
        (click)="exportPdf()"
        [disabled]="!stats() || loading() || printing()"
      >Export PDF</soft-button>
    </page-header>

    @if (printing()) {
      <div class="print-preparing print-hide" role="status" aria-live="polite">
        Préparation du PDF…
      </div>
    }

    <div class="soft-toolbar print-hide">
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
    <period-filter
      class="print-hide"
      [autoSearch]="true"
      (search)="onSearch($event)"
    ></period-filter>

    @if (stats(); as s) {
      @if (applied(); as a) {
        <p class="report-context">
          <span>Période : {{ fmtDateFr(a.from) }} → {{ fmtDateFr(a.to) }}</span>
          <span>Matricules : {{ matriculeLabel(a) }}</span>
          <span>Regroupement : {{ periodeLabel(a.periode) }}</span>
          <span>Département : {{ a.departement || 'Tous' }}</span>
          <span>Catégorie : {{ categorieLabel(a.categorie) }}</span>
          <span>Données chargées le {{ loadedAtLabel() }}</span>
        </p>
      }

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
        @if (axe() === 'departement') {
          <soft-bar-chart
            [items]="departementBars()"
            color="var(--gold-warm)"
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
          [paginate]="!printing()"
        ></data-table>
      </soft-card>

      <h2 class="section-title">Absences et retards par période</h2>
      <soft-card padding="md">
        <data-table
          [columns]="periodeColumns"
          [rows]="periodeRows()"
          [loading]="loading()"
          [paginate]="!printing()"
        ></data-table>
      </soft-card>

      <h2 class="section-title">Assiduité — les plus ponctuels</h2>
      <soft-card padding="md">
        <data-table
          [columns]="assiduiteColumns"
          [rows]="plusPonctuelsRows()"
          [loading]="loading()"
          [paginate]="!printing()"
          emptyText="Aucun pointage sur la période."
        ></data-table>
      </soft-card>

      <h2 class="section-title">Assiduité — les moins ponctuels</h2>
      <soft-card padding="md">
        <data-table
          [columns]="assiduiteColumns"
          [rows]="moinsPonctuelsRows()"
          [loading]="loading()"
          [paginate]="!printing()"
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
      .report-context {
        display: flex;
        flex-wrap: wrap;
        gap: 4px 18px;
        margin: 0 0 18px;
        padding: 10px 14px;
        background: var(--surface-light);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-md);
        color: var(--text-secondary);
        font-size: 12.5px;
        line-height: 1.5;
      }
      .print-preparing {
        position: fixed;
        inset: 0;
        z-index: 9999;
        display: grid;
        place-items: center;
        background: rgba(255, 255, 255, 0.86);
        color: var(--navy-950);
        font-family: var(--font-heading);
        font-size: 18px;
        font-weight: 600;
      }
      .kpi-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
        gap: 16px;
        margin-bottom: 14px;
      }
      .kpi-detail {
        margin: 0 0 28px;
        padding: 12px 16px;
        background: var(--surface-light);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-md);
        color: var(--text-secondary);
        font-size: 13.5px;
        line-height: 1.5;
      }
      .kpi-detail strong { color: var(--navy-950); }
      .kpi-grid strong {
        display: block;
        font-family: var(--font-heading);
        font-size: 28px;
        font-weight: 700;
        line-height: 1.2;
        color: var(--navy-950);
        font-variant-numeric: tabular-nums;
        margin: 4px 0 2px;
      }
      .kpi-grid small { color: var(--text-muted); font-size: 12px; }
      .kpi-label {
        display: block;
        font-size: 12px;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        color: var(--text-secondary);
      }
      .section-title {
        font-family: var(--font-heading);
        font-size: 18px;
        font-weight: 600;
        color: var(--navy-950);
        margin: 28px 0 14px;
      }
      .chart-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
        gap: 18px;
        margin-bottom: 28px;
      }
      .chart-grid .section-title { margin-top: 0; }
      .sub-title {
        font-family: var(--font-heading);
        margin: 22px 0 12px;
        font-size: 15px;
        font-weight: 600;
        color: var(--navy-900);
      }
      .muted { color: var(--text-muted); }

      @media print {
        .report-context {
          margin-bottom: 10px;
          padding: 8px 10px;
          font-size: 10.5px;
        }
        .kpi-grid {
          grid-template-columns: repeat(5, 1fr);
          gap: 8px;
          margin-bottom: 8px;
        }
        .kpi-grid strong { font-size: 19px; margin: 2px 0; }
        .kpi-grid small { font-size: 9.5px; }
        .kpi-label { font-size: 9px; }
        .kpi-detail {
          margin-bottom: 14px;
          padding: 8px 10px;
          font-size: 11px;
        }
        .chart-grid {
          grid-template-columns: 1fr;
          gap: 10px;
          margin-bottom: 14px;
        }
        .section-title {
          font-size: 15px;
          margin: 16px 0 8px;
        }
        .sub-title { margin: 12px 0 8px; font-size: 13px; }
        .print-preparing { display: none !important; }
      }
    `,
  ],
})
export class AnalyticsPage implements OnInit {
  private readonly svc = inject(DashboardService);
  private readonly excel = inject(ExcelExportService);
  private readonly cdr = inject(ChangeDetectorRef);

  readonly loading = signal(false);
  readonly erreur = signal(false);
  readonly stats = signal<DashboardStats | null>(null);
  readonly filters = signal<DashboardFilters | null>(null);
  readonly applied = signal<ReportContext | null>(null);
  readonly loadedAt = signal<Date | null>(null);
  readonly printing = signal(false);

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
    { id: 'departement', label: 'Département' },
  ];
  readonly axe = signal('categorie');

  readonly titreAxe = computed(() =>
    this.axe() === 'departement' ? 'Département' : 'Catégorie',
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
        color: 'var(--gold-warm)',
        values: stats.series.map((s) => s.heuresSupplementaires),
      },
    ];
  });

  readonly categorieBars = computed<ChartPoint[]>(() =>
    (this.stats()?.repartition.parCategorie ?? []).map((s) => ({ label: s.libelle, value: s.effectif })),
  );
  readonly departementBars = computed<ChartPoint[]>(() =>
    (this.stats()?.repartition.parDepartement ?? []).map((s) => ({ label: s.libelle, value: s.effectif })),
  );

  readonly repartitionRows = computed(() => {
    const repartition = this.stats()?.repartition;
    if (!repartition) return [];
    const slices =
      this.axe() === 'departement'
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

  /** Date ISO (yyyy-mm-dd) affichée en jj/mm/aaaa, comme dans le contexte du rapport. */
  fmtDateFr(value: string | null | undefined): string {
    if (!value) return '—';
    const date = parseLocalDate(value.slice(0, 10));
    return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString('fr-FR');
  }

  periodeLabel(value: string): string {
    return this.periodeOptions.find((option) => option.value === value)?.label ?? value;
  }

  categorieLabel(id: number | null): string {
    if (id == null) return 'Toutes';
    return this.filters()?.categories.find((c) => c.id === id)?.libelle ?? String(id);
  }

  matriculeLabel(context: ReportContext): string {
    if (!context.matriculeFrom && !context.matriculeTo) return 'Tous';
    return `${context.matriculeFrom ?? '—'} → ${context.matriculeTo ?? '—'}`;
  }

  loadedAtLabel(): string {
    const loadedAt = this.loadedAt();
    return loadedAt ? loadedAt.toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' }) : '—';
  }

  /**
   * Export PDF : le document est l'impression du DOM affiché (mise en page A4 dédiée).
   * Pendant la préparation, la pagination des tableaux est désactivée pour imprimer tout le jeu
   * filtré ; le thème est forcé en clair et restauré à la fermeture de la boîte d'impression.
   */
  exportPdf(): void {
    const stats = this.stats();
    const context = this.applied();
    if (!stats || !context || this.loading() || this.printing()) return;

    const root = document.documentElement;
    const previousTheme = root.getAttribute('data-theme');
    const previousTitle = document.title;
    let restored = false;

    const restore = (): void => {
      if (restored) return;
      restored = true;
      document.title = previousTitle;
      if (previousTheme) root.setAttribute('data-theme', previousTheme);
      else root.removeAttribute('data-theme');
      this.printing.set(false);
      this.cdr.detectChanges();
    };

    window.addEventListener('afterprint', restore, { once: true });
    // Dernier recours si le navigateur n'émet pas afterprint : ne pas laisser l'écran en mode export.
    window.setTimeout(restore, 300_000);

    this.printing.set(true);
    document.title = this.printFileName(context);
    root.setAttribute('data-theme', 'light');
    this.cdr.detectChanges();

    try {
      window.print();
    } catch {
      restore();
    }
  }

  /**
   * Export Excel : classeur multi-feuilles construit à partir des données déjà affichées.
   * Les deux axes de répartition sont exportés, indépendamment de l'onglet affiché.
   */
  exportExcel(): void {
    const stats = this.stats();
    const context = this.applied();
    if (!stats || !context || this.loading()) return;

    const sheets: ExcelSheet[] = [
      {
        sheetName: 'Synthèse',
        headers: ['Indicateur', 'Valeur'],
        rows: this.syntheseRows(stats, context),
      },
      {
        sheetName: 'Périodes',
        headers: [
          'Période',
          'Absences',
          'Retards',
          'Minutes retard',
          'Heures travaillées',
          'Heures supplémentaires',
        ],
        rows: this.periodeExportRows(stats),
      },
      ...this.repartitionSheets(stats),
      {
        sheetName: 'Assiduité',
        headers: [
          'Classement',
          'Matricule',
          'Nom',
          'Ponctualité (%)',
          'Jours pointés',
          'Retards',
          'Absences',
        ],
        rows: this.assiduiteExportRows(stats),
      },
    ];

    this.excel.downloadWorkbook(this.printFileName(context), sheets);
  }

  /**
   * Une feuille par axe de répartition (Catégorie, Département). Les deux viennent de la même
   * réponse du serveur : l'onglet actif ne conditionne pas le contenu du classeur.
   */
  private repartitionSheets(stats: DashboardStats): ExcelSheet[] {
    const toSheet = (axe: string, slices: DashboardSlice[]): ExcelSheet => ({
      sheetName: `Répartition - ${axe}`,
      headers: [
        axe,
        'Effectif actif',
        'Absences',
        'Retards',
        'Minutes retard',
        'Heures supplémentaires',
      ],
      rows: slices.map((slice) => [
        slice.libelle,
        slice.effectif,
        slice.absences,
        slice.retards,
        slice.minutesRetard,
        slice.heuresSupplementaires,
      ]),
    });

    return [
      toSheet('Catégorie', stats.repartition.parCategorie),
      toSheet('Département', stats.repartition.parDepartement),
    ];
  }

  private printFileName(context: ReportContext): string {
    return `analyse-periode_${context.from}_${context.to}`;
  }

  /** Feuille Synthèse : contexte des filtres, KPI et totaux secondaires (mêmes valeurs qu'à l'écran). */
  private syntheseRows(stats: DashboardStats, context: ReportContext): (string | number)[][] {
    const kpi = stats.kpi;
    return [
      ['Analyse de la période', ''],
      ['Période', `${this.fmtDateFr(context.from)} → ${this.fmtDateFr(context.to)}`],
      ['Matricules', this.matriculeLabel(context)],
      ['Regroupement', this.periodeLabel(context.periode)],
      ['Département', context.departement || 'Tous'],
      ['Catégorie', this.categorieLabel(context.categorie)],
      ['Données chargées le', this.loadedAtLabel()],
      ['', ''],
      ['Effectif actif', kpi.cartesActives],
      ['Cartes sur la période', stats.totalEmployes],
      ["Taux d'absentéisme (%)", kpi.tauxAbsenteisme],
      ["Jours d'absence", kpi.joursAbsence],
      ['Jours planifiés', kpi.joursPlanifies],
      ['Taux de ponctualité (%)', kpi.tauxPonctualite],
      ['Jours en retard', kpi.joursRetard],
      ['Jours pointés', kpi.joursTravailles],
      ['Heures supplémentaires', kpi.heuresSupplementaires],
      ['Heures travaillées', kpi.heuresTravaillees],
      ['Semaines validées', kpi.semainesValidees],
      ['Semaines en attente', kpi.semainesEnAttente],
      ['', ''],
      ['Total absences', stats.absences.total],
      ['Absences explicites', stats.absences.explicites],
      ['Absences planifiées sans pointage', stats.absences.planifiesSansPointage],
      ['Total retards', stats.retards.total],
      ['Minutes de retard cumulées', stats.retards.totalMinutes],
      ['Anomalies', kpi.anomalies],
      ['Corrections manuelles', kpi.correctionsManuelles],
    ];
  }

  /** Feuille Périodes : les deux courbes (absences/retards et heures) sur les mêmes libellés. */
  private periodeExportRows(stats: DashboardStats): (string | number)[][] {
    const retards = new Map(stats.retards.stats.map((row) => [row.periode, row]));
    const heures = new Map(stats.series.map((row) => [row.periode, row]));
    const rows: (string | number)[][] = stats.absences.stats.map((absence) => {
      const retard = retards.get(absence.periode);
      const serie = heures.get(absence.periode);
      return [
        absence.periode,
        absence.count,
        retard?.count ?? 0,
        retard?.totalMinutes ?? 0,
        serie?.heures ?? 0,
        serie?.heuresSupplementaires ?? 0,
      ];
    });

    // Filet de sécurité si une période n'apparaissait que dans les séries d'heures.
    const connues = new Set(rows.map((row) => row[0]));
    for (const serie of stats.series) {
      if (connues.has(serie.periode)) continue;
      rows.push([serie.periode, 0, 0, 0, serie.heures, serie.heuresSupplementaires]);
    }
    return rows;
  }

  private assiduiteExportRows(stats: DashboardStats): (string | number)[][] {
    const toRow = (row: DashboardAssiduiteRow, classement: string): (string | number)[] => [
      classement,
      row.matricule,
      row.nom ?? '',
      row.tauxPonctualite,
      row.joursTravailles,
      row.joursRetard,
      row.joursAbsence,
    ];
    return [
      ...stats.assiduite.plusPonctuels.map((row) => toRow(row, 'Plus ponctuel')),
      ...stats.assiduite.moinsPonctuels.map((row) => toRow(row, 'Moins ponctuel')),
    ];
  }

  private load(): void {
    if (!this.lastFilter) return;
    const context: ReportContext = {
      from: this.lastFilter.from,
      to: this.lastFilter.to,
      matriculeFrom: this.lastFilter.matriculeFrom ?? null,
      matriculeTo: this.lastFilter.matriculeTo ?? null,
      periode: this.periode,
      departement: this.departement,
      categorie: this.categorie,
    };
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
          // Le contexte et les données sont mis à jour ensemble : les exports décrivent
          // exactement le rapport affiché, jamais une requête intermédiaire.
          this.stats.set(s);
          this.applied.set(context);
          this.loadedAt.set(new Date());
          this.loading.set(false);
        },
        error: () => {
          this.erreur.set(true);
          this.loading.set(false);
        },
      });
  }
}
