import { ChangeDetectionStrategy, Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SessionStore } from '../../core/session.store';
import { ToastService } from '../../core/toast.service';
import { CodeConstante } from '../../shared/models';
import { CodeConstantesService } from '../../shared/services/catalog.service';
import { SageDatabasesService } from '../../shared/services/users.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal, SoftSelect } from '../../shared/components';
import { asRow } from '../../shared/utils/date';

@Component({
  selector: 'app-code-constantes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftCard, DataTable, SoftModal, SoftInput, SoftSelect, SoftButton],
  template: `
    <page-header title="Configuration Code Constante" [subtitle]="isOther() ? 'Codes constantes de la base RH / paie AUTRE active' : 'Mapping types HS → CodeConstante SAGE'"></page-header>
    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()">
        <ng-template #actions let-row>
          <soft-button size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
        </ng-template>
      </data-table>
    </soft-card>
    <soft-modal [open]="modal()" title="Modifier code constante" (closed)="modal.set(false)">
      <div class="form-grid">
        <p class="muted">{{ form.intitule || form.categorie }}</p>
        @if (isOther()) {
          <soft-select label="Table correspondante" [required]="true" [(ngModel)]="form.tableCible" name="tableCible"
            [options]="otherTableOptions()" placeholder="Sélectionner une table" (ngModelChange)="onOtherTableChanged($event)"></soft-select>
          @if (form.tableCible) {
            <soft-select label="Code constante" [required]="true" [(ngModel)]="form.codeConstante" name="codeConstante"
              [options]="otherCodeOptions()" placeholder="Sélectionner un code"></soft-select>
          } @else {
            <p class="muted">Choisissez d'abord la table correspondante.</p>
          }
        } @else if (hasSageList()) {
          <soft-select label="Code constante SAGE" [(ngModel)]="form.codeConstante" name="codeConstante" [options]="sageOptions()"></soft-select>
        } @else {
          <soft-input label="Code constante SAGE" [(ngModel)]="form.codeConstante" name="codeConstante"></soft-input>
        }
      </div>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [`.form-grid { display: grid; gap: 14px; } .muted { margin: 0; color: var(--muted, #6b7280); font-weight: 600; }`],
})
export class CodeConstantesPage {
  private readonly svc = inject(CodeConstantesService);
  private readonly databasesSvc = inject(SageDatabasesService);
  private readonly session = inject(SessionStore);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly sageOptions = signal<{ value: string; label: string }[]>([]);
  readonly otherTableOptions = signal<{ value: string; label: string }[]>([]);
  readonly otherCodeOptions = signal<{ value: string; label: string }[]>([]);
  readonly hasSageList = signal(false);
  readonly isOther = signal(false);
  form: Partial<CodeConstante> = {};
  readonly columns: Column[] = [
    { key: 'intitule', label: 'Catégorie' },
    { key: 'codeConstante', label: 'Code constante' },
  ];

  constructor() {
    effect(() => {
      this.session.activeSageDb();
      this.loadActiveDatabaseConfiguration();
    });
  }

  private loadActiveDatabaseConfiguration(): void {
    this.modal.set(false);
    this.rows.set([]);
    this.sageOptions.set([]);
    this.otherTableOptions.set([]);
    this.otherCodeOptions.set([]);
    this.hasSageList.set(false);
    this.databasesSvc.list().subscribe({
      next: (databases) => {
        this.isOther.set(databases.some((database) => database.nomBd === this.session.activeSageDb() && database.typeBase === 'AUTRE'));
        if (this.isOther()) this.loadOtherConfiguration();
        else this.loadSageConfiguration();
      },
      error: () => this.loadSageConfiguration(),
    });
  }

  private loadSageConfiguration(): void {
    this.svc.sageOptions().subscribe({
      next: (opts) => {
        this.hasSageList.set(opts.length > 0);
        this.sageOptions.set(opts.map((o) => ({ value: o.code, label: o.intitule ? `${o.code} — ${o.intitule}` : o.code })));
      },
      error: () => { this.hasSageList.set(false); this.sageOptions.set([]); },
    });
    this.load();
  }

  private loadOtherConfiguration(): void {
    this.svc.otherTables().subscribe({
      next: (tables) => this.otherTableOptions.set(tables.map((value) => ({ value, label: value }))),
      error: (error) => this.toast.warning(error?.error?.title || error?.error || 'Configurez d’abord la source de code constante dans Bases RH / paie.'),
    });
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (items) => { this.rows.set(items.map(asRow)); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }

  openEdit(row: Record<string, unknown>): void {
    this.editId.set(row['id'] as number);
    this.form = { ...(row as unknown as CodeConstante) };
    const current = this.form.codeConstante;
    if (this.isOther()) {
      if (this.form.tableCible) this.loadOtherCodes(this.form.tableCible, current);
    } else if (this.hasSageList() && current && !this.sageOptions().some((option) => option.value === current)) {
      this.sageOptions.set([{ value: current, label: current }, ...this.sageOptions()]);
    }
    this.modal.set(true);
  }

  onOtherTableChanged(table: string | null): void {
    this.form.codeConstante = '';
    this.otherCodeOptions.set([]);
    if (table) this.loadOtherCodes(table);
  }

  private loadOtherCodes(table: string, selected?: string): void {
    this.svc.otherValues(table).subscribe({
      next: (values) => {
        this.otherCodeOptions.set(values.map((value) => ({ value, label: value })));
        if (selected && values.some((value) => value === selected)) this.form.codeConstante = selected;
        else if (selected) {
          this.form.codeConstante = '';
          this.toast.warning('Le code enregistré n’existe plus dans cette table : sélectionnez-en un nouveau.');
        }
      },
      error: (error) => {
        this.otherCodeOptions.set([]);
        this.toast.warning(error?.error?.title || error?.error || 'La colonne de code constante est indisponible dans cette table.');
      },
    });
  }

  save(): void {
    const id = this.editId();
    if (id == null) return;
    if (this.isOther() && (!this.form.tableCible || !this.form.codeConstante)) {
      this.toast.warning('Choisissez une table et un code constante.');
      return;
    }
    this.saving.set(true);
    this.svc.update(id, { ...(this.form as CodeConstante), id }).subscribe({
      next: () => {
        this.saving.set(false);
        this.modal.set(false);
        this.toast.success('Enregistré.');
        this.load();
      },
      error: (error) => {
        this.saving.set(false);
        this.toast.error(error?.error?.title || error?.error || 'Enregistrement impossible.');
      },
    });
  }
}
