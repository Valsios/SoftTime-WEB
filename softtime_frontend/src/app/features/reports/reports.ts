import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ReportFilter, PageChangeEvent } from '../../shared/models';
import { ReportsService } from '../../shared/services/reports.service';
import {
  Column,
  DataTable,
  PageHeader,
  SoftCard,
  SoftSelect,
  PeriodFilter,
  SoftButton,
} from '../../shared/components';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { fmtDate, fmtHeure } from '../../shared/utils/date';
import { errorMessage } from '../../core/error.logic';
import {
  ABSENCES_JUSTIFIEES,
  JUSTIFIEES_COLUMNS,
  JUSTIFIEES_EMPTY,
  JUSTIFIEES_NOTICE,
} from './reports.logic';

export const REPORTS = [
  { id: 'pointage', label: 'État pointage' },
  { id: 'absences', label: 'État absences' },
  { id: ABSENCES_JUSTIFIEES, label: 'Absences justifiées' },
  { id: 'retards', label: 'État retards' },
  { id: 'hs-recap', label: 'Récap HS EXO/IMPO' },
  { id: 'heures-semaine', label: 'Heures par semaine' },
  { id: 'heures-dimanche', label: 'Heures dimanche' },
] as const;

const ABSENCE_COLUMNS: Column[] = [
  { key: 'matricule', label: 'Matricule' },
  { key: 'nom', label: 'Nom', format: (v: unknown) => String(v ?? '') },
  { key: 'prenom', label: 'Prénom', format: (v: unknown) => String(v ?? '') },
  { key: 'departement', label: 'Département', format: (v: unknown) => String(v ?? '') },
  {
    key: 'date',
    label: 'Date',
    format: (v: unknown) => (typeof v === 'string' ? fmtDate(v) : String(v ?? '')),
  },
  {
    key: 'dureeHeures',
    label: 'Durée (h)',
    format: (v: unknown) => (v != null && v !== '' ? `${v} h` : '-'),
  },
];

const ABSENCE_EXPORT_COLUMNS: Column[] = [
  { key: 'matricule', label: 'Matricule' },
  { key: 'nom', label: 'Nom', format: (v: unknown) => (v != null ? String(v) : '') },
  { key: 'prenom', label: 'Prénom', format: (v: unknown) => (v != null ? String(v) : '') },
  { key: 'departement', label: 'Département', format: (v: unknown) => (v != null ? String(v) : '') },
  {
    key: 'date',
    label: 'Date',
    format: (v: unknown) => (typeof v === 'string' ? fmtDate(v) : (v != null ? String(v) : '')),
  },
  {
    key: 'dureeHeures',
    label: 'Durée (h)',
    format: (v: unknown) => (v != null && v !== '' ? String(v) : ''),
  },
];

const RETARD_COLUMNS: Column[] = [
  { key: 'matricule', label: 'Matricule' },
  { key: 'nom', label: 'Nom', format: (v: unknown) => String(v ?? '') },
  { key: 'prenom', label: 'Prénom', format: (v: unknown) => String(v ?? '') },
  { key: 'departement', label: 'Département', format: (v: unknown) => String(v ?? '') },
  {
    key: 'date',
    label: 'Date',
    format: (v: unknown) => (typeof v === 'string' ? fmtDate(v) : String(v ?? '')),
  },
  { key: 'heureEntree', label: "Heure d'entrée", format: (v: unknown) => fmtHeure(v) || '-' },
  { key: 'heurePrevue', label: 'Heure prévue', format: (v: unknown) => fmtHeure(v) || '-' },
  {
    key: 'toleranceMinutes',
    label: 'Tolérance (min)',
    align: 'right',
    format: (v: unknown) => (v != null ? String(v) : ''),
  },
  {
    key: 'minutesRetard',
    label: 'Retard (min)',
    align: 'right',
    format: (v: unknown) => (v != null ? String(v) : ''),
  },
];

const RETARD_EXPORT_COLUMNS: Column[] = [
  { key: 'matricule', label: 'Matricule' },
  { key: 'nom', label: 'Nom', format: (v: unknown) => (v != null ? String(v) : '') },
  { key: 'prenom', label: 'Prénom', format: (v: unknown) => (v != null ? String(v) : '') },
  { key: 'departement', label: 'Département', format: (v: unknown) => (v != null ? String(v) : '') },
  {
    key: 'date',
    label: 'Date',
    format: (v: unknown) => (typeof v === 'string' ? fmtDate(v) : (v != null ? String(v) : '')),
  },
  { key: 'heureEntree', label: "Heure d'entrée", format: (v: unknown) => fmtHeure(v) },
  { key: 'heurePrevue', label: 'Heure prévue', format: (v: unknown) => fmtHeure(v) },
  {
    key: 'toleranceMinutes',
    label: 'Tolérance (min)',
    format: (v: unknown) => (v != null && v !== '' ? String(v) : ''),
  },
  {
    key: 'minutesRetard',
    label: 'Retard (min)',
    format: (v: unknown) => (v != null && v !== '' ? String(v) : ''),
  },
];

@Component({
  selector: 'app-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftCard, DataTable, SoftSelect, PeriodFilter, SoftButton],
  template: `
    <page-header title="Rapports" subtitle="États et extractions GTA">
      <soft-button variant="secondary" [loading]="excel.exporting()" (click)="exportExcel()">Export Excel</soft-button>
    </page-header>

    <div class="soft-toolbar">
      <soft-select
        label="Type de rapport"
        [(ngModel)]="reportType"
        name="reportType"
        (ngModelChange)="onTypeChange($event)"
        [options]="reportOptions"
      ></soft-select>
    </div>

    @if (justifiees()) {
      <p class="reports-note">{{ notice }}</p>
    }

    <period-filter [showDepartement]="true" (search)="onSearch($event)"></period-filter>

    @if (erreur(); as message) {
      <div class="reports-alert" role="alert">{{ message }}</div>
    } @else {
    <soft-card>
      <data-table
        [columns]="columns()"
        [rows]="rows()"
        [loading]="loading()"
        [serverSide]="true"
        [total]="total()"
        [serverPageSize]="pageSize"
        [serverPage]="page()"
        [emptyText]="emptyText()"
        [(searchTerm)]="tableSearch"
        (searchSubmit)="onTableSearch($event)"
        (pageChange)="onPageChange($event)"
      ></data-table>
    </soft-card>
    }
  `,
  styles: [
    `.reports-note { margin: 0 0 12px; color: var(--text-muted); font-size: 13px; }
     .reports-alert { padding: 10px 14px; font-size: 13px; border-radius: var(--radius-md); border: 1px solid; color: var(--danger); background: var(--danger-bg); border-color: var(--danger-border); }`,
  ],
})
export class ReportsPage {
  private readonly svc = inject(ReportsService);
  protected readonly excel = inject(ExcelExportService);

  readonly loading = signal(false);
  pageSize = 50;
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly columns = signal<Column[]>([]);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly tableSearch = signal('');
  reportType: string = 'pointage';
  /** Type courant en signal (le template OnPush en dépend) ; `reportType` reste le modèle de la liste déroulante. */
  private readonly typeCourant = signal<string>('pointage');
  readonly justifiees = computed(() => this.typeCourant() === ABSENCES_JUSTIFIEES);
  protected readonly notice = JUSTIFIEES_NOTICE;
  /** Message d'erreur du serveur pour l'état « Absences justifiées » (remplace le tableau : jamais une liste vide muette). */
  readonly erreur = signal<string | null>(null);
  private readonly recherche = signal(false);
  /** Texte d'état vide : propre à « Absences justifiées », et seulement après une recherche. */
  readonly emptyText = computed(() => (this.justifiees() && this.recherche() ? JUSTIFIEES_EMPTY : 'Aucune donnée.'));
  readonly reportOptions = REPORTS.map((r) => ({ value: r.id, label: r.label }));
  private lastFilter: ReportFilter | null = null;
  private requestSeq = 0;

  onSearch(filter: ReportFilter): void {
    this.lastFilter = filter;
    this.recherche.set(true);
    this.page.set(1);
    this.loadPage();
  }

  onPageChange(event: PageChangeEvent): void {
    this.page.set(event.page);
    this.pageSize = event.pageSize;
    this.loadPage();
  }

  onTableSearch(term: string): void {
    this.tableSearch.set(term);
    this.page.set(1);
    this.loadPage();
  }

  onTypeChange(type: string): void {
    this.reportType = type;
    this.typeCourant.set(type);
    this.erreur.set(null);
    this.columns.set([]);
    this.tableSearch.set('');
    this.page.set(1);
    if (this.lastFilter) this.loadPage();
  }

  private loadPage(): void {
    if (!this.lastFilter) return;
    const seq = ++this.requestSeq;
    this.erreur.set(null);
    this.loading.set(true);
    this.svc.paged(this.reportType, {
      ...this.lastFilter,
      search: this.tableSearch(),
      page: this.page(),
      pageSize: this.pageSize,
    }).subscribe({
      next: (r) => {
        if (seq !== this.requestSeq) return;
        const rows = r.items as Record<string, unknown>[];
        this.rows.set(rows);
        if (!this.columns().length) {
          if (this.reportType === ABSENCES_JUSTIFIEES) {
            this.columns.set(JUSTIFIEES_COLUMNS);
          } else if (this.reportType === 'absences') {
            this.columns.set(ABSENCE_COLUMNS);
          } else if (this.reportType === 'retards') {
            this.columns.set(RETARD_COLUMNS);
          } else {
            this.columns.set(this.inferColumns(rows));
          }
        }
        this.total.set(r.total);
        this.page.set(r.page);
        this.loading.set(false);
      },
      error: (err) => {
        if (seq !== this.requestSeq) return;
        this.loading.set(false);
        if (this.reportType === ABSENCES_JUSTIFIEES) {
          // Ex. 400 « congés SAGE indisponibles » : message du serveur tel quel, aucune liste vide silencieuse.
          this.rows.set([]);
          this.total.set(0);
          this.erreur.set(errorMessage(err));
        }
      },
    });
  }

  private resolve(type: string) {
    switch (type) {
      case 'absences': return this.svc.absences;
      case ABSENCES_JUSTIFIEES: return this.svc.absencesJustifiees;
      case 'retards': return this.svc.retards;
      case 'hs-recap': return this.svc.hsRecap;
      case 'heures-semaine': return this.svc.heuresSemaine;
      case 'heures-dimanche': return this.svc.heuresDimanche;
      default: return this.svc.pointage;
    }
  }

  private inferColumns(rows: Record<string, unknown>[]): Column[] {
    if (!rows.length) return [{ key: '_', label: 'Aucune donnée' }];
    return Object.keys(rows[0]).slice(0, 12).map((k) => ({
      key: k,
      label: k,
      format: (v: unknown) => (typeof v === 'string' && v.includes('T') ? fmtDate(v) : String(v ?? '')),
    }));
  }

  exportExcel(): void {
    if (!this.lastFilter) {
      this.excel.download(`rapport-${this.reportType}`, this.columns(), this.rows(), 'Données', this.tableSearch());
      return;
    }
    const filter = this.lastFilter;
    const fn = this.resolve(this.reportType);
    this.excel.track(fn.call(this.svc, filter)).subscribe({
      next: (items) => {
        const rows = items as Record<string, unknown>[];
        const cols =
          this.reportType === ABSENCES_JUSTIFIEES
            ? JUSTIFIEES_COLUMNS
            : this.reportType === 'absences'
              ? ABSENCE_EXPORT_COLUMNS
              : this.reportType === 'retards'
                ? RETARD_EXPORT_COLUMNS
                : this.inferColumns(rows);
        this.excel.download(`rapport-${this.reportType}`, cols, rows, 'Données', this.tableSearch());
      },
    });
  }
}
