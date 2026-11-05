import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { PagedResult, ReportFilter, PageChangeEvent } from '../../shared/models';
import { HistoryService } from '../../shared/services/reports.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import {
  Column,
  DataTable,
  PageHeader,
  SoftButton,
  SoftCard,
  SoftTabs,
  PeriodFilter,
} from '../../shared/components';
import { asRow, fmtDate } from '../../shared/utils/date';

type Segment = 'pointages' | 'anomalies' | 'hs';

/**
 * Archives des suppressions : ce qui a été retiré des pointages, des anomalies et des calculs HS.
 * Le filtre de période porte sur la **date de suppression** de l'archive.
 * Seul le segment actif est chargé, page par page.
 */
@Component({
  selector: 'app-history',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, SoftButton, SoftCard, DataTable, SoftTabs, PeriodFilter],
  template: `
    <page-header title="Historique" subtitle="Archives des suppressions — période = date de suppression">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button variant="danger" [loading]="busy()" (click)="purgeAll()">Purger l'archive</soft-button>
    </page-header>

    <soft-tabs [tabs]="tabs" [(activeTab)]="activeTab" (activeTabChange)="onTabChange($event)" />
    <period-filter [autoSearch]="true" (search)="onSearch($event)"></period-filter>

    <soft-card>
      <data-table
        [columns]="columns()"
        [rows]="rows()"
        [loading]="loading()"
        [serverSide]="true"
        [total]="total()"
        [serverPageSize]="pageSize"
        [serverPage]="page()"
        [(searchTerm)]="tableSearch"
        (searchSubmit)="onTableSearch($event)"
        (pageChange)="onPageChange($event)"
      >
        <ng-template #actions let-row>
          <soft-button size="sm" variant="danger" (click)="removeOne(row)">Supprimer</soft-button>
        </ng-template>
      </data-table>
    </soft-card>
  `,
})
export class HistoryPage {
  private readonly svc = inject(HistoryService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(false);
  readonly busy = signal(false);
  pageSize = 50;
  readonly activeTab = signal<string>('pointages');
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly tableSearch = signal('');
  readonly tabs = [
    { id: 'pointages', label: 'Pointages' },
    { id: 'anomalies', label: 'Anomalies' },
    { id: 'hs', label: 'Calculs HS' },
  ];

  private readonly columnsBySegment: Record<Segment, Column[]> = {
    pointages: [
      { key: 'matricule', label: 'Matricule' },
      { key: 'date', label: 'Date pointage', format: fmtDate },
      { key: 'heure', label: 'Heure' },
      { key: 'type', label: 'Type' },
      { key: 'supprimeLe', label: 'Supprimé le', format: fmtDate },
    ],
    anomalies: [
      { key: 'matricule', label: 'Matricule' },
      { key: 'dateIn', label: 'Entrée', format: fmtDate },
      { key: 'hEntree', label: 'H. entrée' },
      { key: 'hSortie', label: 'H. sortie' },
      { key: 'intituleAbsence', label: 'Absence' },
      { key: 'supprimeLe', label: 'Supprimé le', format: fmtDate },
    ],
    hs: [
      { key: 'matricule', label: 'Matricule' },
      { key: 'periodeDebut', label: 'Début', format: fmtDate },
      { key: 'periodeFin', label: 'Fin', format: fmtDate },
      { key: 'exo130', label: 'EXO 130%' },
      { key: 'exo150', label: 'EXO 150%' },
      { key: 'i130', label: 'IMPO 130%' },
      { key: 'i150', label: 'IMPO 150%' },
      { key: 'supprimeLe', label: 'Supprimé le', format: fmtDate },
    ],
  };

  readonly columns = computed<Column[]>(() => this.columnsBySegment[this.activeTab() as Segment] ?? []);

  private lastFilter: ReportFilter | null = null;
  private requestSeq = 0;

  onSearch(filter: ReportFilter): void {
    this.lastFilter = filter;
    this.page.set(1);
    this.loadPage();
  }

  onTabChange(tab: string): void {
    this.activeTab.set(tab);
    this.tableSearch.set('');
    if (!this.lastFilter) return;
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

  private loadPage(): void {
    if (!this.lastFilter) return;
    const filter: ReportFilter = {
      ...this.lastFilter,
      search: this.tableSearch(),
      page: this.page(),
      pageSize: this.pageSize,
    };
    const seq = ++this.requestSeq;
    this.loading.set(true);
    const onError = () => { if (seq === this.requestSeq) this.loading.set(false); };
    const segment = this.activeTab();
    if (segment === 'pointages') {
      this.svc.pointagesPaged(filter).subscribe({ next: (r) => this.applyPage(r, seq), error: onError });
    } else if (segment === 'anomalies') {
      this.svc.anomaliesPaged(filter).subscribe({ next: (r) => this.applyPage(r, seq), error: onError });
    } else {
      this.svc.hsPaged(filter).subscribe({ next: (r) => this.applyPage(r, seq), error: onError });
    }
  }

  private applyPage<T extends object>(r: PagedResult<T>, seq: number): void {
    if (seq !== this.requestSeq) return;
    this.rows.set(r.items.map((item) => asRow(item)));
    this.total.set(r.total);
    this.page.set(r.page);
    this.loading.set(false);
  }

  async removeOne(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer définitivement cette archive ?'))) return;
    const segment = this.activeTab();
    this.svc.removeOne(segment, row['id'] as number).subscribe({
      next: () => {
        this.toast.success('Archive supprimée.');
        this.loadPage();
      },
    });
  }

  async purgeAll(): Promise<void> {
    const segment = this.activeTab();
    const libelle = this.tabs.find((t) => t.id === segment)?.label ?? segment;
    if (!(await this.confirm.ask(`Purger définitivement toutes les archives « ${libelle} » ?`))) return;
    this.busy.set(true);
    this.svc.purgeAll(segment).subscribe({
      next: (r) => {
        this.busy.set(false);
        this.toast.success(r.message || `${r.supprimees} archive(s) purgée(s).`);
        this.page.set(1);
        this.loadPage();
      },
      error: () => this.busy.set(false),
    });
  }

  exportExcel(): void {
    const filter = this.lastFilter;
    const segment = this.activeTab();
    const fileName = `historique-${segment}`;
    if (!filter) {
      this.excel.download(fileName, this.columns(), this.rows(), 'Données', this.tableSearch());
      return;
    }
    if (segment === 'pointages') {
      this.svc.pointages(filter).subscribe({
        next: (items) => this.excel.download(fileName, this.columns(), items.map(asRow), 'Données', this.tableSearch()),
      });
    } else if (segment === 'anomalies') {
      this.svc.anomalies(filter).subscribe({
        next: (items) => this.excel.download(fileName, this.columns(), items.map(asRow), 'Données', this.tableSearch()),
      });
    } else {
      this.svc.hs(filter).subscribe({
        next: (items) => this.excel.download(fileName, this.columns(), items.map(asRow), 'Données', this.tableSearch()),
      });
    }
  }
}
