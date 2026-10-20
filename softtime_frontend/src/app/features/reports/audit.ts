import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { AuditConfig, AuditTypeOption } from '../../shared/models';
import { AuditService } from '../../shared/services/reports.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal, SoftSelect, SoftTabs, SoftToggle } from '../../shared/components';
import { asRow, fmtDate, today } from '../../shared/utils/date';

@Component({
  selector: 'app-audit',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput, SoftSelect, SoftToggle, SoftTabs],
  template: `
    <page-header title="Audit" subtitle="Configuration et journal">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      @if (activeTab() === 'config') {
        <soft-button (click)="openCreate()">Ajouter config</soft-button>
      }
    </page-header>

    <soft-tabs [tabs]="tabs" [(activeTab)]="activeTab" />

    @if (activeTab() === 'config') {
      <soft-card padding="md">
        <data-table [columns]="configColumns" [rows]="configRows()" [loading]="loadingConfig()">
          <ng-template #actions let-row>
            <soft-button size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
          </ng-template>
        </data-table>
      </soft-card>
    } @else {
      <soft-card padding="md">
        <data-table [columns]="eventColumns" [rows]="eventRows()" [loading]="loadingEvents()"></data-table>
      </soft-card>
    }

    <soft-modal [open]="modal()" title="Config audit" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-input label="Table" [(ngModel)]="form.table" name="table"></soft-input>
        <soft-input label="Colonne" [(ngModel)]="form.column" name="column"></soft-input>
        <soft-select
          label="Type"
          [required]="true"
          placeholder="Sélectionner un type..."
          [(ngModel)]="form.type"
          name="type"
          [options]="typeOptions()"
        ></soft-select>
        @if (loadTypeError()) {
          <p class="error-msg">{{ loadTypeError() }}</p>
        }
        <soft-input label="Base" [(ngModel)]="form.base" name="base"></soft-input>
        <label class="toggle-row"><span>Activé</span><soft-toggle [(ngModel)]="form.enabled" name="enabled"></soft-toggle></label>
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" [disabled]="isSaveDisabled()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [
    `
      .form-grid { display: grid; gap: 14px; }
      .toggle-row { display: flex; justify-content: space-between; align-items: center; font-weight: 600; }
      .error-msg { color: #dc2626; font-size: 0.85rem; margin-top: -6px; }
    `,
  ],
})
export class AuditPage implements OnInit {
  private readonly svc = inject(AuditService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);

  readonly loadingConfig = signal(true);
  readonly loadingEvents = signal(false);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly configRows = signal<Record<string, unknown>[]>([]);
  readonly eventRows = signal<Record<string, unknown>[]>([]);
  readonly rawTypeOptions = signal<AuditTypeOption[]>([]);
  readonly typeOptions = signal<AuditTypeOption[]>([]);
  readonly loadTypeError = signal<string | null>(null);

  form: Partial<AuditConfig> = {};
  readonly activeTab = signal('config');
  readonly tabs = [
    { id: 'config', label: 'Configuration' },
    { id: 'events', label: 'Événements' },
  ];

  readonly configColumns: Column[] = [
    { key: 'table', label: 'Table' },
    { key: 'column', label: 'Colonne' },
    { key: 'enabled', label: 'Activé', format: (v) => (v ? 'Oui' : 'Non') },
    { key: 'base', label: 'Base' },
  ];

  readonly eventColumns: Column[] = [
    { key: 'date', label: 'Date', format: fmtDate },
    { key: 'user', label: 'Utilisateur' },
    { key: 'table', label: 'Table' },
    { key: 'column', label: 'Colonne' },
    { key: 'action', label: 'Action' },
    { key: 'oldValue', label: 'Ancien' },
    { key: 'newValue', label: 'Nouveau' },
  ];

  ngOnInit(): void {
    this.svc.config().subscribe({
      next: (items) => { this.configRows.set(items.map(asRow)); this.loadingConfig.set(false); },
      error: () => this.loadingConfig.set(false),
    });
    this.loadTypes();
    this.loadEvents();
  }

  loadTypes(): void {
    this.svc.types().subscribe({
      next: (items) => {
        this.rawTypeOptions.set(items);
        this.typeOptions.set(items);
        this.loadTypeError.set(null);
      },
      error: () => {
        this.loadTypeError.set('Impossible de charger la liste des types.');
      },
    });
  }

  loadEvents(): void {
    this.loadingEvents.set(true);
    const to = today();
    const fromDate = new Date();
    fromDate.setDate(fromDate.getDate() - 30);
    const from = fromDate.toISOString().slice(0, 10);
    this.svc.events({ from, to }).subscribe({
      next: (items) => { this.eventRows.set(items.map(asRow)); this.loadingEvents.set(false); },
      error: () => this.loadingEvents.set(false),
    });
  }

  openCreate(): void {
    this.editId.set(null);
    this.form = { enabled: true, type: '' };
    this.typeOptions.set(this.rawTypeOptions());
    this.modal.set(true);
  }

  openEdit(row: Record<string, unknown>): void {
    this.editId.set(row['id'] as number);
    const cfg = { ...(row as unknown as AuditConfig) };
    this.form = cfg;
    const currentType = (cfg.type ?? '').trim();
    const exists = this.rawTypeOptions().some(
      (o) => o.value.toLowerCase() === currentType.toLowerCase()
    );
    if (currentType && !exists) {
      this.typeOptions.set([
        { value: currentType, label: `${currentType} (ancienne valeur)` },
        ...this.rawTypeOptions(),
      ]);
    } else {
      this.typeOptions.set(this.rawTypeOptions());
    }
    this.modal.set(true);
  }

  isSaveDisabled(): boolean {
    return this.saving() || !this.form.type || this.form.type.trim() === '';
  }

  save(): void {
    if (this.isSaveDisabled()) return;
    this.saving.set(true);
    const id = this.editId();
    const dto = {
      ...this.form,
      type: this.form.type?.trim(),
    } as AuditConfig;
    const req = id
      ? this.svc.updateConfig(id, { ...dto, id })
      : this.svc.createConfig({ ...dto, id: 0 });
    req.subscribe({
      next: () => {
        this.saving.set(false);
        this.modal.set(false);
        this.toast.success('Enregistré.');
        this.loadingConfig.set(true);
        this.svc.config().subscribe({
          next: (items) => { this.configRows.set(items.map(asRow)); this.loadingConfig.set(false); },
          error: () => this.loadingConfig.set(false),
        });
      },
      error: () => this.saving.set(false),
    });
  }

  exportExcel(): void {
    if (this.activeTab() === 'config') {
      this.excel.download('audit-config', this.configColumns, this.configRows());
    } else {
      this.excel.download('audit-evenements', this.eventColumns, this.eventRows());
    }
  }
}
