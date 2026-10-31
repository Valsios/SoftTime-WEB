import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { OvertimeDetail, PeriodRequest, PageChangeEvent } from '../../shared/models';
import { OvertimeService } from '../../shared/services/processing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftModal, PeriodFilter } from '../../shared/components';
import { asRow, fmtDate, isMaxOneMonth } from '../../shared/utils/date';
import { SessionStore } from '../../core/session.store';

@Component({
  selector: 'app-overtime',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, PeriodFilter],
  template: `
    <page-header title="Heures supplémentaires" subtitle="Calcul EXO/IMPO — période maximale d'un mois">
      <soft-button variant="danger" [loading]="busy()" (click)="purge()">Purger</soft-button>
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button variant="accent" [loading]="busy()" (click)="syncPayroll()">{{ syncLabel() }}</soft-button>
      <soft-button [loading]="busy()" (click)="calculate()">Calculer</soft-button>
    </page-header>

    <period-filter [autoSearch]="true" [showDepartement]="true" (search)="onSearch($event)"></period-filter>

    <soft-card>
      <data-table
        [columns]="columns"
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
          <soft-button size="sm" variant="ghost" (click)="openDetail(row)">Détail</soft-button>
        </ng-template>
      </data-table>
    </soft-card>

    <soft-modal [open]="detailOpen()" title="Détail du calcul HS" (closed)="detailOpen.set(false)">
      @if (detail(); as d) {
        <p class="detail-head">
          Matricule <strong>{{ d.matricule }}</strong> — du {{ d.from | date: 'dd/MM/yyyy' }}
          au {{ d.to | date: 'dd/MM/yyyy' }}
        </p>
        <h3 class="detail-title">Jours</h3>
        <data-table [columns]="dayColumns" [rows]="dayRows()" [loading]="detailLoading()"></data-table>
        <h3 class="detail-title">Semaines (EXO/IMPO)</h3>
        <data-table [columns]="weekColumns" [rows]="weekRows()" [loading]="detailLoading()"></data-table>
        @if (d.exoImpo; as exo) {
          <h3 class="detail-title">Allocation enregistrée</h3>
          <p class="detail-head">
            EXO 130 : <strong>{{ exo.exo130 ?? 0 }}</strong> — EXO 150 : <strong>{{ exo.exo150 ?? 0 }}</strong>
            — IMPO 130 : <strong>{{ exo.i130 ?? 0 }}</strong> — IMPO 150 : <strong>{{ exo.i150 ?? 0 }}</strong>
          </p>
        } @else {
          <p class="detail-head">Aucune allocation EXO/IMPO enregistrée sur cette période.</p>
        }
      } @else {
        <p class="detail-head">Chargement…</p>
      }
      <div footer>
        <soft-button variant="ghost" (click)="detailOpen.set(false)">Fermer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [`
    .detail-head { margin: 0 0 12px; }
    .detail-title { font-size: 15px; margin: 18px 0 10px; }
  `],
})
export class OvertimePage {
  private readonly svc = inject(OvertimeService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionStore);

  readonly busy = signal(false);
  readonly loading = signal(false);
  pageSize = 50;
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly page = signal(1);
  readonly total = signal(0);
  readonly tableSearch = signal('');
  readonly detailOpen = signal(false);
  readonly detailLoading = signal(false);
  readonly detail = signal<OvertimeDetail | null>(null);
  private lastFilter: PeriodRequest | null = null;
  private requestSeq = 0;

  readonly currentDatabase = () => this.session.databases().find((db) => db.nomBd === this.session.activeSageDb());
  readonly isStandard = () => this.currentDatabase()?.typeBase !== 'AUTRE';
  readonly syncLabel = () => this.isStandard() ? 'Synchroniser vers SAGE' : 'Synchroniser vers la base';

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'exo130', label: 'EXO 130%' },
    { key: 'exo150', label: 'EXO 150%' },
    { key: 'i130', label: 'IMPO 130%' },
    { key: 'i150', label: 'IMPO 150%' },
    { key: 'ferie', label: 'Férié' },
    { key: 'nuit', label: 'Nuit' },
    { key: 'dim', label: 'Dimanche' },
    { key: 'retard', label: 'Retard' },
    { key: 'absence', label: 'Absence' },
  ];

  private validatePeriod(filter: PeriodRequest | null): filter is PeriodRequest {
    if (!filter) {
      this.toast.warning('Définissez une période.');
      return false;
    }
    if (filter.from > filter.to) {
      this.toast.warning('La date de début doit être antérieure ou égale à la date de fin.');
      return false;
    }
    if (!isMaxOneMonth(filter.from, filter.to)) {
      this.toast.warning('La période sélectionnée ne doit pas dépasser un mois.');
      return false;
    }
    return true;
  }

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    if (!this.validatePeriod(filter)) {
      this.rows.set([]);
      this.total.set(0);
      return;
    }
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
    const seq = ++this.requestSeq;
    this.loading.set(true);
    this.svc.listPaged({
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

  calculate(): void {
    if (this.busy() || !this.validatePeriod(this.lastFilter)) return;
    this.busy.set(true);
    this.svc.calculate(this.lastFilter).subscribe({
      next: () => {
        this.busy.set(false);
        this.toast.success('Calcul terminé.');
        this.page.set(1);
        this.loadPage();
      },
      error: () => this.busy.set(false),
    });
  }

  async purge(): Promise<void> {
    if (this.busy() || !this.validatePeriod(this.lastFilter)) return;
    if (!(await this.confirm.ask('Purger les HS de la période ?'))) return;
    this.busy.set(true);
    this.svc.purge(this.lastFilter).subscribe({
      next: () => { this.busy.set(false); this.toast.success('HS purgées.'); this.rows.set([]); this.total.set(0); this.page.set(1); },
      error: () => this.busy.set(false),
    });
  }

  async syncPayroll(): Promise<void> {
    if (this.busy() || !this.validatePeriod(this.lastFilter)) return;
    const database = this.currentDatabase()?.nomBd ?? this.session.activeSageDb() ?? 'la base active';
    const question = this.isStandard()
      ? 'Pousser les HS vers SAGE (T_CUMSAL) ?'
      : `Écrire les HS dans la base ${database} ?`;
    if (!(await this.confirm.ask(question))) return;
    this.busy.set(true);
    this.svc.syncPayroll(this.lastFilter).subscribe({
      next: (result) => { this.busy.set(false); this.toast.success(result.message); },
      error: () => this.busy.set(false),
    });
  }

  exportExcel(): void {
    if (!this.lastFilter) {
      this.excel.download('heures-supplementaires', this.columns, this.rows(), 'Données', this.tableSearch());
      return;
    }
    this.loading.set(true);
    this.svc.list(this.lastFilter).subscribe({
      next: (items) => {
        this.loading.set(false);
        this.excel.download('heures-supplementaires', this.columns, items.map(asRow));
      },
      error: () => this.loading.set(false),
    });
  }

  readonly dayColumns: Column[] = [
    { key: 'date', label: 'Date', format: fmtDate },
    { key: 'entree', label: 'Entrée' },
    { key: 'sortie', label: 'Sortie' },
    { key: 'heuresReelles', label: 'Réel' },
    { key: 'heuresTravaillees', label: 'Retenu' },
    { key: 'heuresSupJournalieres', label: 'Sup. jour' },
    { key: 'heuresNuit', label: 'Nuit' },
    { key: 'retard', label: 'Retard' },
    { key: 'conge', label: 'Congé' },
    { key: 'ferie', label: 'Férié' },
    { key: 'dimanche', label: 'Dimanche' },
  ];

  readonly weekColumns: Column[] = [
    { key: 'semaine', label: 'Semaine' },
    { key: 'debut', label: 'Début', format: fmtDate },
    { key: 'fin', label: 'Fin', format: fmtDate },
    { key: 'hs', label: 'HS' },
    { key: 'hs1', label: 'HS 130 %' },
    { key: 'hs2', label: 'HS 150 %' },
  ];

  readonly dayRows = () => (this.detail()?.jours ?? []).map(asRow);
  readonly weekRows = () => (this.detail()?.semaines ?? []).map(asRow);

  /** Détail du calcul HS du salarié de la ligne (jours, semaines EXO/IMPO, allocation). */
  openDetail(row: Record<string, unknown>): void {
    const matricule = row['matricule'];
    if (typeof matricule !== 'string' || !matricule) {
      this.toast.warning('Matricule introuvable sur cette ligne.');
      return;
    }
    if (!this.validatePeriod(this.lastFilter)) return;
    this.detail.set(null);
    this.detailOpen.set(true);
    this.detailLoading.set(true);
    this.svc.detail(matricule, this.lastFilter!.from, this.lastFilter!.to).subscribe({
      next: (d) => { this.detail.set(d); this.detailLoading.set(false); },
      error: () => this.detailLoading.set(false),
    });
  }
}
