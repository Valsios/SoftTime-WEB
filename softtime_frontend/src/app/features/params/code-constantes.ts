import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { CodeConstante } from '../../shared/models';
import { CodeConstantesService } from '../../shared/services/catalog.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal, SoftSelect } from '../../shared/components';
import { asRow } from '../../shared/utils/date';

@Component({
  selector: 'app-code-constantes',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftCard, DataTable, SoftModal, SoftInput, SoftSelect, SoftButton],
  template: `
    <page-header title="Configuration Code Constante" subtitle="Mapping types HS → CodeConstante SAGE"></page-header>
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
        @if (sageOptions().length) {
          <soft-select
            label="Code constante SAGE"
            [(ngModel)]="form.codeConstante"
            name="codeConstante"
            [options]="sageOptions()"
          ></soft-select>
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
export class CodeConstantesPage implements OnInit {
  private readonly svc = inject(CodeConstantesService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly sageOptions = signal<{ value: string; label: string }[]>([]);
  form: Partial<CodeConstante> = {};
  readonly columns: Column[] = [
    { key: 'intitule', label: 'Catégorie' },
    { key: 'codeConstante', label: 'Code constante' },
  ];

  ngOnInit(): void {
    this.svc.sageOptions().subscribe({
      next: (opts) =>
        this.sageOptions.set(
          opts.map((o) => ({
            value: o.code,
            label: o.intitule ? `${o.code} — ${o.intitule}` : o.code,
          })),
        ),
      error: () => this.sageOptions.set([]),
    });
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (items) => {
        this.rows.set(items.map(asRow));
        this.loading.set(false);
      },
      error: () => this.loading.set(false),
    });
  }

  openEdit(row: Record<string, unknown>): void {
    this.editId.set(row['id'] as number);
    this.form = { ...(row as unknown as CodeConstante) };
    const current = this.form.codeConstante;
    if (current && !this.sageOptions().some((o) => o.value === current)) {
      this.sageOptions.set([{ value: current, label: current }, ...this.sageOptions()]);
    }
    this.modal.set(true);
  }

  save(): void {
    const id = this.editId();
    if (id == null) return;
    this.saving.set(true);
    this.svc.update(id, { ...(this.form as CodeConstante), id }).subscribe({
      next: () => {
        this.saving.set(false);
        this.modal.set(false);
        this.toast.success('Enregistré.');
        this.load();
      },
      error: () => this.saving.set(false),
    });
  }
}
