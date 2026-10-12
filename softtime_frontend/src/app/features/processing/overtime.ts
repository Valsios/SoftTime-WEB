import { DatePipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { OvertimeDetail, PeriodRequest } from '../../shared/models';
import { OvertimeService } from '../../shared/services/processing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftModal, PeriodFilter } from '../../shared/components';
import { asRow, fmtDate, isMondayToSundayWeek } from '../../shared/utils/date';
import { SessionStore } from '../../core/session.store';

@Component({
  selector: 'app-overtime',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, PeriodFilter],
  template: `
    <page-header title="Heures supplémentaires" subtitle="Calcul EXO/IMPO — période lundi à dimanche (7 jours)">
      <soft-button variant="danger" [loading]="busy()" (click)="purge()">Purger</soft-button>
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button variant="accent" [loading]="busy()" (click)="syncPayroll()">{{ syncLabel() }}</soft-button>
      <soft-button [loading]="busy()" (click)="calculate()">Calculer</soft-button>
    </page-header>

    <period-filter [autoSearch]="true" [mondaySundayWeek]="true" (search)="onSearch($event)"></period-filter>

    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="busy()">
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
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly detailOpen = signal(false);
  readonly detailLoading = signal(false);
  readonly detail = signal<OvertimeDetail | null>(null);
  private lastFilter: PeriodRequest | null = null;

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

  private requireWeek(filter: PeriodRequest | null): filter is PeriodRequest {
    if (!filter) {
      this.toast.warning('Définissez une période.');
      return false;
    }
    if (!isMondayToSundayWeek(filter.from, filter.to)) {
      this.toast.warning('La date début doit être un lundi et la date fin le dimanche de la même semaine.');
      return false;
    }
    return true;
  }

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    if (!this.requireWeek(filter)) {
      this.rows.set([]);
      return;
    }
    if (this.busy()) return;
    this.busy.set(true);
    this.svc.list(filter).subscribe({
      next: (items) => {
        this.rows.set(items.map(asRow));
        this.busy.set(false);
      },
      error: () => this.busy.set(false),
    });
  }

  calculate(): void {
    if (this.busy() || !this.requireWeek(this.lastFilter)) return;
    this.busy.set(true);
    this.svc.calculate(this.lastFilter).subscribe({
      next: (items) => { this.rows.set(items.map(asRow)); this.busy.set(false); this.toast.success('Calcul terminé.'); },
      error: () => this.busy.set(false),
    });
  }

  async purge(): Promise<void> {
    if (this.busy() || !this.requireWeek(this.lastFilter)) return;
    if (!(await this.confirm.ask('Purger les HS de la période ?'))) return;
    this.busy.set(true);
    this.svc.purge(this.lastFilter).subscribe({
      next: () => { this.busy.set(false); this.toast.success('HS purgées.'); this.rows.set([]); },
      error: () => this.busy.set(false),
    });
  }

  async syncPayroll(): Promise<void> {
    if (this.busy() || !this.requireWeek(this.lastFilter)) return;
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
    this.excel.download('heures-supplementaires', this.columns, this.rows());
  }

  readonly dayColumns: Column[] = [
    { key: 'date', label: 'Date', format: fmtDate },
    { key: 'entree', label: 'Entrée' },
    { key: 'sortie', label: 'Sortie' },
    { key: 'heuresTravaillees', label: 'Heures' },
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
    if (!this.requireWeek(this.lastFilter)) return;
    this.detail.set(null);
    this.detailOpen.set(true);
    this.detailLoading.set(true);
    this.svc.detail(matricule, this.lastFilter!.from, this.lastFilter!.to).subscribe({
      next: (d) => { this.detail.set(d); this.detailLoading.set(false); },
      error: () => this.detailLoading.set(false),
    });
  }
}
