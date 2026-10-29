import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { ConfirmService } from '../../core/confirm.service';
import { SessionStore } from '../../core/session.store';
import { Droit } from '../../core/rights';
import { EmployeePlanning, PeriodRequest, PlanningEmployee, PageChangeEvent } from '../../shared/models';
import { EmployeePlanningService, ShiftsService } from '../../shared/services/processing.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import {
  Column,
  DataTable,
  ImportTemplatesModal,
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
import { toastImportResult } from '../../shared/utils/import-result';

@Component({
  selector: 'app-employee-planning',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    CommonModule,
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
    ImportTemplatesModal,
  ],
  template: `
    <page-header title="Planning salariés" subtitle="Tous les salariés, même sans planning">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <soft-button *ngIf="canCreate()" (click)="openCreate()">Ajouter un planning</soft-button>
      <label class="file-btn">
        <input #planningFile type="file" accept=".xlsx,.xls" hidden (change)="onFile($event)" />
        <soft-button variant="secondary" (click)="planningFile.click()">Import Excel</soft-button>
      </label>
      <label class="file-btn">
        <input #categoryFile type="file" accept=".csv" hidden (change)="onGenerateFromCategoryFile($event)" />
        <soft-button variant="secondary" (click)="categoryFile.click()">Générer par catégorie (CSV)</soft-button>
      </label>
      <label class="file-btn">
        <input #generateFile type="file" accept=".csv" hidden (change)="onGenerateFile($event)" />
        <soft-button variant="secondary" (click)="generateFile.click()">Générer par matricule (CSV)</soft-button>
      </label>
      <soft-button variant="ghost" (click)="templatesOpen.set(true)">Modèles d'import</soft-button>
    </page-header>

    <period-filter [showMatricule]="false" (search)="onSearch($event)"></period-filter>

    <soft-tabs [tabs]="tabs" [(activeTab)]="activeTab" />

    @if (activeTab() === 'employees') {
      <soft-card padding="md">
        <data-table
          [columns]="employeeColumns"
          [rows]="employeeRows()"
          [loading]="loadingPeople()"
          [serverSide]="true"
          [total]="employeeTotal()"
          [serverPageSize]="employeePageSize"
          [serverPage]="employeePage()"
          (pageChange)="onEmployeePageChange($event)"
        >
          <ng-template #actions let-row>
            <soft-button *ngIf="canCreate()" size="sm" (click)="openCreate(row)">Planifier</soft-button>
          </ng-template>
        </data-table>
      </soft-card>
    } @else {
      <soft-card padding="md">
        <data-table
          [columns]="planningColumns"
          [rows]="planningRows()"
          [loading]="loading()"
          [serverSide]="true"
          [total]="planningTotal()"
          [serverPageSize]="planningPageSize"
          [serverPage]="planningPage()"
          (pageChange)="onPlanningPageChange($event)"
        >
          <ng-template #actions let-row>
            <div class="soft-actions" style="justify-content: end">
              <soft-button *ngIf="canUpdate()" size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
              <soft-button *ngIf="canDelete()" size="sm" variant="danger" (click)="remove(row)">Supprimer</soft-button>
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
    <app-import-templates-modal
      [open]="templatesOpen()"
      [keys]="importKeys"
      (closed)="templatesOpen.set(false)"
    />
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
  private readonly session = inject(SessionStore);

  readonly canCreate = computed(() => this.session.hasPermission('PROC:EMPLOYEE_PLANNING:CREATE', Droit.Processing));
  readonly canUpdate = computed(() => this.session.hasPermission('PROC:EMPLOYEE_PLANNING:UPDATE', Droit.Processing));
  readonly canDelete = computed(() => this.session.hasPermission('PROC:EMPLOYEE_PLANNING:DELETE', Droit.Processing));

  readonly loading = signal(false);
  readonly loadingPeople = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly templatesOpen = signal(false);
  readonly importKeys = [
    'planning-matricule',
    'planning-categorie',
    'planning-generate-category',
    'planning-generate-matricule',
  ];
  employeePageSize = 50;
  planningPageSize = 50;
  readonly employeeRows = signal<Record<string, unknown>[]>([]);
  readonly planningRows = signal<Record<string, unknown>[]>([]);
  readonly employeePage = signal(1);
  readonly employeeTotal = signal(0);
  readonly planningPage = signal(1);
  readonly planningTotal = signal(0);
  readonly employeeOptions = signal<{ value: number; label: string }[]>([]);
  readonly shiftOptions = signal<{ value: number; label: string }[]>([]);
  private employees: PlanningEmployee[] = [];
  private lastFilter: PeriodRequest = { from: monthStart(), to: today() };
  private employeeSeq = 0;
  private planningSeq = 0;
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
    this.loadEmployeeRows();
    this.loadPlanning();
  }

  onSearch(filter: PeriodRequest): void {
    this.lastFilter = filter;
    this.employeePage.set(1);
    this.planningPage.set(1);
    this.loadEmployeeRows();
    this.loadPlanning();
  }

  onEmployeePageChange(event: PageChangeEvent): void {
    this.employeePage.set(event.page);
    this.employeePageSize = event.pageSize;
    this.loadEmployeeRows();
  }

  onPlanningPageChange(event: PageChangeEvent): void {
    this.planningPage.set(event.page);
    this.planningPageSize = event.pageSize;
    this.loadPlanning();
  }

  loadEmployees(): void {
    this.svc.employees().subscribe({
      next: (items) => {
        this.employees = items;
        this.employeeOptions.set(
          items.map((e) => ({
            value: e.cardPaieId,
            label: `${e.matricule ?? e.cardPaieId} — ${[e.nom, e.prenom].filter(Boolean).join(' ')}`.trim(),
          })),
        );
      },
    });
  }

  /** Grille des salariés : le nombre de jours planifiés est calculé côté serveur. */
  private loadEmployeeRows(): void {
    const seq = ++this.employeeSeq;
    this.loadingPeople.set(true);
    this.svc.employeesPaged(
      this.lastFilter.from,
      this.lastFilter.to,
      this.employeePage(),
      this.employeePageSize,
    ).subscribe({
      next: (r) => {
        if (seq !== this.employeeSeq) return;
        this.employeeRows.set(r.items.map(asRow));
        this.employeeTotal.set(r.total);
        this.employeePage.set(r.page);
        this.loadingPeople.set(false);
      },
      error: () => { if (seq === this.employeeSeq) this.loadingPeople.set(false); },
    });
  }

  loadPlanning(): void {
    const seq = ++this.planningSeq;
    this.loading.set(true);
    this.svc.listPaged({
      from: this.lastFilter.from,
      to: this.lastFilter.to,
      page: this.planningPage(),
      pageSize: this.planningPageSize,
    }).subscribe({
      next: (r) => {
        if (seq !== this.planningSeq) return;
        this.planningRows.set(r.items.map(asRow));
        this.planningTotal.set(r.total);
        this.planningPage.set(r.page);
        this.loading.set(false);
      },
      error: () => { if (seq === this.planningSeq) this.loading.set(false); },
    });
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
        this.loadEmployeeRows();
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
        this.loadEmployeeRows();
      },
    });
  }

  exportExcel(): void {
    if (this.activeTab() === 'employees') {
      this.svc.employeesStats(this.lastFilter.from, this.lastFilter.to).subscribe({
        next: (items) => this.excel.download('salaries-planning', this.employeeColumns, items.map(asRow)),
      });
    } else {
      this.svc.list(this.lastFilter.from, this.lastFilter.to).subscribe({
        next: (items) => this.excel.download('lignes-planning', this.planningColumns, items.map(asRow)),
      });
    }
  }

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.svc.importExcel(file).subscribe({
      next: (r) => {
        toastImportResult(
          this.toast,
          r,
          `${r.imported} ligne(s) de planning importée(s), ${r.skipped} élément(s) ignoré(s).`,
        );
        this.activeTab.set('planning');
        this.loadPlanning();
        this.loadEmployeeRows();
      },
    });
    input.value = '';
  }

  /** Génération du planning d'une semaine pour toute une catégorie (CSV). */
  onGenerateFromCategoryFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.svc.generateFromCategory(file).subscribe({
      next: (r) => {
        toastImportResult(this.toast, r, `${r.imported} jour(s) généré(s), ${r.skipped} ignoré(s).`);
        this.activeTab.set('planning');
        this.loadPlanning();
        this.loadEmployeeRows();
      },
    });
    input.value = '';
  }

  /** Génération du planning d'une semaine par matricule (CSV, pause par jour). */
  onGenerateFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.svc.generate(file).subscribe({
      next: (r) => {
        toastImportResult(this.toast, r, `${r.imported} jour(s) généré(s), ${r.skipped} ignoré(s).`);
        this.activeTab.set('planning');
        this.loadPlanning();
        this.loadEmployeeRows();
      },
    });
    input.value = '';
  }
}
