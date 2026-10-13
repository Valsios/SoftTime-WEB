import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { ReportFilter } from '../../shared/models';
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

    <soft-tabs [tabs]="tabs" [(activeTab)]="activeTab" />
    <period-filter [autoSearch]="true" (search)="onSearch($event)"></period-filter>

    <soft-card>
      <data-table [columns]="columns()" [rows]="rows()" [loading]="loading()">
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
  readonly activeTab = signal<string>('pointages');
  readonly tabs = [
    { id: 'pointages', label: 'Pointages' },
    { id: 'anomalies', label: 'Anomalies' },
    { id: 'hs', label: 'Calculs HS' },
  ];

  /** Les trois jeux sont chargés en même temps : le changement d'onglet est instantané. */
  private readonly data = signal<Record<Segment, Record<string, unknown>[]>>({
    pointages: [],
    anomalies: [],
    hs: [],
  });
  readonly rows = computed(() => this.data()[this.activeTab() as Segment] ?? []);

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

  onSearch(filter: ReportFilter): void {
    this.lastFilter = filter;
    this.loading.set(true);
    let pending = 3;
    const done = () => {
      if (--pending === 0) this.loading.set(false);
    };
    this.svc.pointages(filter).subscribe({
      next: (r) => this.setSegment('pointages', r),
      error: done,
      complete: done,
    });
    this.svc.anomalies(filter).subscribe({
      next: (r) => this.setSegment('anomalies', r),
      error: done,
      complete: done,
    });
    this.svc.hs(filter).subscribe({
      next: (r) => this.setSegment('hs', r),
      error: done,
      complete: done,
    });
  }

  async removeOne(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer définitivement cette archive ?'))) return;
    const segment = this.activeTab();
    this.svc.removeOne(segment, row['id'] as number).subscribe({
      next: () => {
        this.toast.success('Archive supprimée.');
        this.setSegment(segment as Segment, this.rows().filter((x) => x['id'] !== row['id']));
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
        if (this.lastFilter) this.onSearch(this.lastFilter);
      },
      error: () => this.busy.set(false),
    });
  }

  exportExcel(): void {
    this.excel.download(`historique-${this.activeTab()}`, this.columns(), this.rows());
  }

  private setSegment(segment: Segment, rows: readonly object[]): void {
    this.data.update((d) => ({ ...d, [segment]: rows.map(asRow) }));
  }
}
