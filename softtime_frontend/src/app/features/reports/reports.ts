import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ReportFilter } from '../../shared/models';
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
import { fmtDate } from '../../shared/utils/date';

const REPORTS = [
  { id: 'pointage', label: 'État pointage' },
  { id: 'absences', label: 'État absences' },
  { id: 'retards', label: 'État retards' },
  { id: 'hs-recap', label: 'Récap HS EXO/IMPO' },
  { id: 'heures-semaine', label: 'Heures par semaine' },
  { id: 'heures-dimanche', label: 'Heures dimanche' },
] as const;

@Component({
  selector: 'app-reports',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftCard, DataTable, SoftSelect, PeriodFilter, SoftButton],
  template: `
    <page-header title="Rapports" subtitle="États et extractions GTA">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
    </page-header>

    <div class="soft-toolbar">
      <soft-select
        label="Type de rapport"
        [(ngModel)]="reportType"
        name="reportType"
        [options]="reportOptions"
      ></soft-select>
    </div>

    <period-filter [showBranche]="true" (search)="onSearch($event)"></period-filter>

    <soft-card>
      <data-table [columns]="columns()" [rows]="rows()" [loading]="loading()"></data-table>
    </soft-card>
  `,
})
export class ReportsPage {
  private readonly svc = inject(ReportsService);
  private readonly excel = inject(ExcelExportService);

  readonly loading = signal(false);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly columns = signal<Column[]>([]);
  reportType: string = 'pointage';
  readonly reportOptions = REPORTS.map((r) => ({ value: r.id, label: r.label }));

  onSearch(filter: ReportFilter): void {
    this.loading.set(true);
    const fn = this.resolve(this.reportType);
    fn.call(this.svc, filter).subscribe({
      next: (items) => {
        const rows = items as Record<string, unknown>[];
        this.rows.set(rows);
        this.columns.set(this.inferColumns(rows));
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  private resolve(type: string) {
    switch (type) {
      case 'absences': return this.svc.absences;
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
    this.excel.download(`rapport-${this.reportType}`, this.columns(), this.rows());
  }
}
