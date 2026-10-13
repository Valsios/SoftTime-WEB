import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { SessionStore } from '../../core/session.store';
import { Droit } from '../../core/rights';
import { FormsModule } from '@angular/forms';
import { AnomalyUpdate, PeriodRequest } from '../../shared/models';
import { AnomaliesService } from '../../shared/services/processing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import {
  Column,
  DataTable,
  PageHeader,
  SoftButton,
  SoftCard,
  SoftInput,
  SoftModal,
  SoftToggle,
  PeriodFilter,
} from '../../shared/components';
import { asRow, fmtDate } from '../../shared/utils/date';

@Component({
  selector: 'app-anomalies',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
    FormsModule,
    PageHeader,
    SoftButton,
    SoftCard,
    DataTable,
    SoftInput,
    SoftModal,
    SoftToggle,
    PeriodFilter,
  ],
  template: `
    <page-header title="Anomalies" subtitle="Arbitrage des heures en anomalie">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button [loading]="busy()" (click)="validate()">
        Convertir en heures corrigées
      </soft-button>
    </page-header>
    <period-filter (search)="onSearch($event)"></period-filter>
    <soft-card>
      <data-table
        [columns]="columns"
        [rows]="rows()"
        [loading]="loading()"
        [selectable]="true"
        trackKey="id"
        [(selectedKeys)]="selectedKeys"
      >
        <ng-template #actions let-row>
          <soft-button size="sm" variant="ghost" (click)="openEdit(row)">Arbitrer</soft-button>
          <soft-button *ngIf="canDelete()" size="sm" variant="danger" (click)="remove(row)">Supprimer</soft-button>
        </ng-template>
      </data-table>
    </soft-card>

    <soft-modal [open]="modal()" title="Arbitrage de l'anomalie" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-input label="Date entrée" type="date" [(ngModel)]="form.dateIn" name="dateIn"></soft-input>
        <soft-input label="Heure entrée" type="time" [(ngModel)]="form.hEntree" name="hEntree"></soft-input>
        <soft-input label="Début de pause" type="time" [(ngModel)]="form.hPs" name="hPs"></soft-input>
        <soft-input label="Fin de pause" type="time" [(ngModel)]="form.hPe" name="hPe"></soft-input>
        <soft-input label="Date sortie" type="date" [(ngModel)]="form.dateOut" name="dateOut"></soft-input>
        <soft-input label="Heure sortie" type="time" [(ngModel)]="form.hSortie" name="hSortie"></soft-input>
        <soft-input label="Intitulé absence" [(ngModel)]="form.intituleAbsence" name="intituleAbsence"></soft-input>
        <div class="toggles">
          <span><soft-toggle [(ngModel)]="form.absAm"></soft-toggle> Absence matin</span>
          <span><soft-toggle [(ngModel)]="form.absPm"></soft-toggle> Absence après-midi</span>
          <span><soft-toggle [(ngModel)]="form.feriesAm"></soft-toggle> Férié matin</span>
          <span><soft-toggle [(ngModel)]="form.feriesPm"></soft-toggle> Férié après-midi</span>
        </div>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="busy()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [
    `.form-grid { display: grid; gap: 14px; }
     .toggles { display: flex; flex-wrap: wrap; gap: 18px; }
     .toggles span { display: inline-flex; align-items: center; gap: 8px; }`,
  ],
})
export class AnomaliesPage {
  private readonly svc = inject(AnomaliesService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionStore);

  readonly canDelete = computed(() => this.session.hasPermission('PROC:ANOMALIES:DELETE', Droit.Processing));

  readonly loading = signal(false);
  readonly busy = signal(false);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly selectedKeys = signal<string[]>([]);
  readonly modal = signal(false);

  private editId: number | null = null;
  private lastFilter: PeriodRequest | null = null;
  form: Partial<AnomalyUpdate> = {};

  readonly columns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'dateIn', label: 'Entrée', format: fmtDate },
    { key: 'dateOut', label: 'Sortie', format: fmtDate },
    { key: 'hEntree', label: 'H. entrée' },
    { key: 'hPs', label: 'Début pause' },
    { key: 'hPe', label: 'Fin pause' },
    { key: 'hSortie', label: 'H. sortie' },
    { key: 'intituleAbsence', label: 'Absence' },
  ];

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    this.loading.set(true);
    this.svc.list(filter).subscribe({
      next: (items) => {
        this.rows.set(items.map(asRow));
        this.selectedKeys.set([]);
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  openEdit(row: Record<string, unknown>): void {
    this.editId = row['id'] as number;
    this.form = {
      dateIn: this.toDateInput(row['dateIn']),
      dateOut: this.toDateInput(row['dateOut']),
      hEntree: this.toTimeInput(row['hEntree']),
      hPs: this.toTimeInput(row['hPs']),
      hPe: this.toTimeInput(row['hPe']),
      hSortie: this.toTimeInput(row['hSortie']),
      absAm: (row['absAm'] as boolean) ?? false,
      absPm: (row['absPm'] as boolean) ?? false,
      feriesAm: (row['feriesAm'] as boolean) ?? false,
      feriesPm: (row['feriesPm'] as boolean) ?? false,
      intituleAbsence: (row['intituleAbsence'] as string) ?? '',
    };
    this.modal.set(true);
  }

  save(): void {
    if (this.editId == null) return;
    this.busy.set(true);
    this.svc.update(this.editId, this.toPayload()).subscribe({
      next: () => {
        this.busy.set(false);
        this.modal.set(false);
        this.toast.success('Anomalie arbitrée.');
        this.refresh();
      },
      error: () => this.busy.set(false),
    });
  }

  /** Convertit les anomalies cochées en heures corrigées (puis les supprime). */
  async validate(): Promise<void> {
    const ids = this.selectedKeys().map((k) => Number(k));
    if (ids.length === 0) {
      this.toast.warning('Sélectionnez au moins une anomalie.');
      return;
    }
    if (!(await this.confirm.ask(`Convertir ${ids.length} anomalie(s) en heures corrigées ?`))) return;
    this.busy.set(true);
    this.svc.validate(ids).subscribe({
      next: (r) => {
        this.busy.set(false);
        this.toast.success(r.message || `${r.correctionsCreees} correction(s) créée(s).`);
        this.refresh();
      },
      error: () => this.busy.set(false),
    });
  }

  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cette anomalie ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({
      next: () => {
        this.toast.success('Supprimé.');
        this.rows.update((r) => r.filter((x) => x['id'] !== row['id']));
      },
    });
  }

  exportExcel(): void {
    this.excel.download('anomalies', this.columns, this.rows());
  }

  private refresh(): void {
    if (this.lastFilter) this.onSearch(this.lastFilter);
  }

  /** Ne transmet que les valeurs réellement saisies : un champ vide laisse la valeur en base inchangée. */
  private toPayload(): AnomalyUpdate {
    const text = (v: string | null | undefined): string | undefined =>
      v === null || v === undefined || v === '' ? undefined : v;
    return {
      dateIn: text(this.form.dateIn),
      dateOut: text(this.form.dateOut),
      hEntree: text(this.form.hEntree),
      hPs: text(this.form.hPs),
      hPe: text(this.form.hPe),
      hSortie: text(this.form.hSortie),
      absAm: this.form.absAm ?? false,
      absPm: this.form.absPm ?? false,
      feriesAm: this.form.feriesAm ?? false,
      feriesPm: this.form.feriesPm ?? false,
      intituleAbsence: this.form.intituleAbsence ?? '',
    };
  }

  /** « 2026-08-31T00:00:00 » → « 2026-08-31 » (sans conversion de fuseau horaire). */
  private toDateInput(value: unknown): string | null {
    return typeof value === 'string' && value.length >= 10 ? value.slice(0, 10) : null;
  }

  /** « 08:00:00 » → « 08:00 » (valeur attendue par un <input type="time">). */
  private toTimeInput(value: unknown): string | null {
    return typeof value === 'string' && value.length >= 5 ? value.slice(0, 5) : null;
  }
}
