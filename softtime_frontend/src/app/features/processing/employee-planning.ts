import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { ConfirmService } from '../../core/confirm.service';
import { EmployeePlanning, PeriodRequest, PlanningEmployee } from '../../shared/models';
import { EmployeePlanningService, ShiftsService } from '../../shared/services/processing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import {
  Column,
  DataTable,
  PageHeader,
  PeriodFilter,
  SoftButton,
  SoftCard,
  SoftInput,
  SoftModal,
  SoftSelect,
  SoftTabs,
  SoftToggle,
} from '../../shared/components';
import { asRow, fmtDate, monthStart, today } from '../../shared/utils/date';

@Component({
  selector: 'app-employee-planning',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    PageHeader,
    SoftButton,
    SoftCard,
    DataTable,
    PeriodFilter,
    SoftInput,
    SoftModal,
    SoftSelect,
    SoftToggle,
    SoftTabs,
  ],
  template: `
    <page-header title="Planning salariés" subtitle="Tous les salariés, même sans planning">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button (click)="openCreate()">Ajouter un planning</soft-button>
      <label class="file-btn">
        <input #planningFile type="file" accept=".xlsx,.xls" hidden (change)="onFile($event)" />
        <soft-button variant="secondary" (click)="planningFile.click()">Import Excel</soft-button>
      </label>
    </page-header>

    <period-filter [showMatricule]="false" (search)="onSearch($event)"></period-filter>

    <soft-tabs [tabs]="tabs" [(activeTab)]="activeTab" />

    @if (activeTab() === 'employees') {
      <soft-card padding="md">
        <data-table [columns]="employeeColumns" [rows]="employeeRows()" [loading]="loadingPeople()">
          <ng-template #actions let-row>
            <soft-button size="sm" (click)="openCreate(row)">Planifier</soft-button>
          </ng-template>
        </data-table>
      </soft-card>
    } @else {
      <soft-card padding="md">
        <data-table [columns]="planningColumns" [rows]="planningRows()" [loading]="loading()">
          <ng-template #actions let-row>
            <div class="soft-actions" style="justify-content: end">
              <soft-button size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
              <soft-button size="sm" variant="danger" (click)="remove(row)">Supprimer</soft-button>
            </div>
          </ng-template>
        </data-table>
      </soft-card>
    }

    <soft-modal [open]="modal()" title="Planning" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-select
          label="Salarié"
          [(ngModel)]="form.cardPaieId"
          name="cardPaieId"
          [options]="employeeOptions()"
        ></soft-select>
        <soft-input label="Date" type="date" [(ngModel)]="form.dateP" name="dateP"></soft-input>
        <soft-select
          label="Shift"
          [(ngModel)]="form.noShift"
          name="noShift"
          placeholder="Aucun"
          [options]="shiftOptions()"
        ></soft-select>
        <label class="toggle-row">
          <span>Jour off</span>
          <soft-toggle [(ngModel)]="form.off" name="off"></soft-toggle>
        </label>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [
    `
      .form-grid { display: grid; gap: 14px; }
      .toggle-row { display: flex; justify-content: space-between; align-items: center; font-weight: 600; }
      .file-btn { cursor: pointer; }
    `,
  ],
})
export class EmployeePlanningPage implements OnInit {
  private readonly svc = inject(EmployeePlanningService);
  private readonly shiftsSvc = inject(ShiftsService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly loading = signal(false);
  readonly loadingPeople = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly employeeRows = signal<Record<string, unknown>[]>([]);
  readonly planningRows = signal<Record<string, unknown>[]>([]);
  readonly employeeOptions = signal<{ value: number; label: string }[]>([]);
  readonly shiftOptions = signal<{ value: number; label: string }[]>([]);
  private employees: PlanningEmployee[] = [];
  private lastFilter: PeriodRequest = { from: monthStart(), to: today() };
  form: Partial<EmployeePlanning> = {};
  readonly activeTab = signal('employees');
  readonly tabs = [
    { id: 'employees', label: 'Salariés' },
    { id: 'planning', label: 'Lignes de planning' },
  ];

  readonly employeeColumns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'nom', label: 'Nom' },
    { key: 'prenom', label: 'Prénom' },
    { key: 'plannedDays', label: 'Jours planifiés' },
  ];

  readonly planningColumns: Column[] = [
    { key: 'matricule', label: 'Matricule' },
    { key: 'nom', label: 'Nom' },
    { key: 'prenom', label: 'Prénom' },
    { key: 'dateP', label: 'Date', format: fmtDate },
    { key: 'noShift', label: 'Shift' },
    { key: 'off', label: 'Off', format: (v) => (v ? 'Oui' : 'Non') },
  ];

  ngOnInit(): void {
    this.shiftsSvc.list().subscribe({
      next: (shifts) =>
        this.shiftOptions.set(
          shifts.map((s) => ({
            value: s.noShift ?? Number(s.id),
            label: `${s.noShift ?? s.id} — ${s.intitule ?? 'Shift'}`,
          })),
        ),
    });
    this.loadEmployees();
    this.loadPlanning();
  }

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    this.loadPlanning();
  }

  loadEmployees(): void {
    this.loadingPeople.set(true);
    this.svc.employees().subscribe({
      next: (items) => {
        this.employees = items;
        this.employeeOptions.set(
          items.map((e) => ({
            value: e.cardPaieId,
            label: `${e.matricule ?? e.cardPaieId} — ${[e.nom, e.prenom].filter(Boolean).join(' ')}`.trim(),
          })),
        );
        this.refreshEmployeeRows();
        this.loadingPeople.set(false);
      },
      error: () => this.loadingPeople.set(false),
    });
  }

  loadPlanning(): void {
    this.loading.set(true);
    this.svc.list(this.lastFilter.from, this.lastFilter.to).subscribe({
      next: (items) => {
        this.planningRows.set(items.map(asRow));
        this.refreshEmployeeRows();
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  private refreshEmployeeRows(): void {
    const counts = new Map<number, number>();
    for (const row of this.planningRows()) {
      const id = Number(row['cardPaieId']);
      counts.set(id, (counts.get(id) ?? 0) + 1);
    }
    this.employeeRows.set(
      this.employees.map((e) => ({
        cardPaieId: e.cardPaieId,
        matricule: e.matricule,
        nom: e.nom,
        prenom: e.prenom,
        plannedDays: counts.get(e.cardPaieId) ?? 0,
      })),
    );
  }

  openCreate(row?: Record<string, unknown>): void {
    this.form = {
      id: 0,
      cardPaieId: row ? Number(row['cardPaieId']) : this.employees[0]?.cardPaieId,
      dateP: today(),
      off: false,
    };
    this.modal.set(true);
  }

  openEdit(row: Record<string, unknown>): void {
    const date = String(row['dateP'] ?? '');
    this.form = {
      ...(row as unknown as EmployeePlanning),
      dateP: date.includes('T') ? date.slice(0, 10) : date.slice(0, 10),
    };
    this.modal.set(true);
  }

  save(): void {
    if (!this.form.cardPaieId || !this.form.dateP) {
      this.toast.warning('Salarié et date obligatoires.');
      return;
    }
    this.saving.set(true);
    const dto: EmployeePlanning = {
      id: Number(this.form.id ?? 0),
      cardPaieId: Number(this.form.cardPaieId),
      dateP: this.form.dateP,
      noShift: this.form.noShift != null && this.form.noShift !== ('' as unknown) ? Number(this.form.noShift) : null,
      off: !!this.form.off,
    };
    this.svc.save([dto]).subscribe({
      next: () => {
        this.saving.set(false);
        this.modal.set(false);
        this.toast.success('Planning enregistré.');
        this.loadPlanning();
      },
      error: () => this.saving.set(false),
    });
  }

  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cette ligne de planning ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({
      next: () => {
        this.toast.success('Supprimé.');
        this.loadPlanning();
      },
    });
  }

  exportExcel(): void {
    if (this.activeTab() === 'employees') {
      this.excel.download('salaries-planning', this.employeeColumns, this.employeeRows());
    } else {
      this.excel.download('lignes-planning', this.planningColumns, this.planningRows());
    }
  }

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.svc.importExcel(file).subscribe({
      next: (r) => {
        this.toast.success(r.message || `${r.imported} ligne(s) importée(s).`);
        this.activeTab.set('planning');
        this.loadPlanning();
      },
    });
    input.value = '';
  }
}
