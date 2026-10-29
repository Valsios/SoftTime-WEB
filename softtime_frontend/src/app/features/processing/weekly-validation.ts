import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { PeriodRequest, PageChangeEvent } from '../../shared/models';
import { WeeklyValidationService } from '../../shared/services/processing.service';
import { ClockParamsService } from '../../shared/services/catalog.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, PeriodFilter } from '../../shared/components';
import { asRow, fmtDate } from '../../shared/utils/date';

@Component({
  selector: 'app-weekly-validation',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, SoftButton, SoftCard, DataTable, PeriodFilter],
  template: `
    <page-header title="Validation semaine" subtitle="Calcul HT/HS lundi–dimanche — seules les lignes cochées sont validées">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button variant="secondary" [loading]="busy()" (click)="validate(false)">Annuler validation</soft-button>
      <soft-button [loading]="busy()" (click)="validate(true)">Valider semaine</soft-button>
      <soft-button variant="danger" [loading]="busy()" (click)="purge()">Purger la validation</soft-button>
    </page-header>

    <period-filter [mondaySundayWeek]="true" searchLabel="Calculer" (search)="preview($event)"></period-filter>

    <soft-card>
      <p class="hint">
        @if (modeLabel()) { <strong>{{ modeLabel() }}</strong> — }
        {{ selectedKeys().length }} salarié(s) sélectionné(s) — la sélection est conservée entre les pages ;
        « tout sélectionner » porte sur la page affichée.
      </p>
      <data-table
        [columns]="columns"
        [rows]="rows()"
        [loading]="loading()"
        [selectable]="true"
        trackKey="matricule"
        [serverSide]="true"
        [total]="total()"
        [serverPageSize]="pageSize"
        [serverPage]="page()"
        [(searchTerm)]="tableSearch"
        (searchSubmit)="onTableSearch($event)"
        (pageChange)="onPageChange($event)"
        [(selectedKeys)]="selectedKeys"
      ></data-table>
    </soft-card>
  `,
  styles: [`.hint { margin: 0 0 12px; color: var(--text-muted); font-size: 13px; }`],
})
export class WeeklyValidationPage {
  private readonly svc = inject(WeeklyValidationService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly clock = inject(ClockParamsService);

  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly modeLabel = signal('');
  pageSize = 50;
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly tableSearch = signal('');
  readonly selectedKeys = signal<string[]>([]);
  private lastFilter: PeriodRequest | null = null;
  private requestSeq = 0;

  constructor() {
    // Affiche le mode de calcul actif (T_CLOCK.MULTIPOINT) : il change la règle des HS.
    this.clock.get().subscribe({
      next: (c) =>
        this.modeLabel.set(
          c.multiPoint
            ? 'Multi-pointage : HS au-delà de HEURESEMAINE'
            : '2 pointages : somme des dépassements journaliers',
        ),
      error: () => this.modeLabel.set(''),
    });
  }

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'dateDeb', label: 'Début', format: fmtDate },
    { key: 'dateFin', label: 'Fin', format: fmtDate },
    { key: 'ht', label: 'HT' },
    { key: 'hs', label: 'HS' },
    { key: 'nuit', label: 'Maj. Nuit' },
    { key: 'dimanche', label: 'Maj. Dimanche' },
    { key: 'feries', label: 'Maj. Férié' },
    { key: 'retard', label: 'Retard' },
    { key: 'absence', label: 'Absence' },
    { key: 'validated', label: 'Validé', format: (v) => (v ? 'Oui' : 'Non') },
  ];

  preview(filter: PeriodRequest): void {
    this.lastFilter = filter;
    this.selectedKeys.set([]);
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
    this.selectedKeys.set([]);
    this.page.set(1);
    this.loadPage();
  }

  private loadPage(): void {
    if (!this.lastFilter) return;
    const seq = ++this.requestSeq;
    this.loading.set(true);
    this.svc.previewPaged({
      ...this.lastFilter,
      search: this.tableSearch(),
      page: this.page(),
      pageSize: this.pageSize,
    }).subscribe({
      next: (r) => {
        if (seq !== this.requestSeq) return;
        this.rows.set(r.items.map(asRow));
        this.total.set(r.total);
        this.page.set(r.page);
        this.loading.set(false);
      },
      error: () => { if (seq === this.requestSeq) this.loading.set(false); },
    });
  }

  async validate(ok: boolean): Promise<void> {
    if (!this.lastFilter) {
      this.toast.warning("Lancez d'abord un aperçu.");
      return;
    }
    const selectedMatricules = this.selectedKeys();
    if (selectedMatricules.length === 0) {
      this.toast.warning('Cochez au moins une ligne.');
      return;
    }
    const msg = ok
      ? `Valider la semaine pour ${selectedMatricules.length} salarié(s) sélectionné(s) ?`
      : `Annuler la validation pour ${selectedMatricules.length} salarié(s) sélectionné(s) ?`;
    if (!(await this.confirm.ask(msg))) return;
    this.busy.set(true);
    const body = { ...this.lastFilter, selectedMatricules };
    const req = ok ? this.svc.validate(body) : this.svc.unvalidate(body);
    req.subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success(ok ? 'Lignes sélectionnées validées.' : 'Validation annulée pour la sélection.');
        this.selectedKeys.set([]);
        this.loadPage();
      },
      error: () => this.busy.set(false),
    });
  }

  exportExcel(): void {
    if (!this.lastFilter) {
      this.excel.download('validation-semaine', this.columns, this.rows(), 'Données', this.tableSearch());
      return;
    }
    this.loading.set(true);
    this.svc.preview(this.lastFilter).subscribe({
      next: (items) => {
        this.loading.set(false);
        // L'export reprend exactement la grille, y compris les semaines déjà validées.
        this.excel.download('validation-semaine', this.columns, items.map(asRow), 'Données', this.tableSearch());
      },
      error: () => this.loading.set(false),
    });
  }

  /** Purge la validation de la semaine : supprime T_HS_VALIDER et déverrouille les heures corrigées. */
  async purge(): Promise<void> {
    if (!this.lastFilter) {
      this.toast.warning("Lancez d'abord un aperçu.");
      return;
    }
    if (!(await this.confirm.ask(
      'Purger la validation de cette semaine ? Les heures corrigées seront déverrouillées et la semaine redeviendra calculable.'))) return;
    this.busy.set(true);
    this.svc.purge(this.lastFilter).subscribe({
      next: (r) => {
        this.busy.set(false);
        this.toast.success(r.message);
        this.loadPage();
      },
      error: () => this.busy.set(false),
    });
  }
}
