import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { SessionStore } from '../../core/session.store';
import { Droit } from '../../core/rights';
import { Majoration, PageChangeEvent } from '../../shared/models';
import { MajorationsService } from '../../shared/services/catalog.service';
import { Column, DataTable, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal } from '../../shared/components';
import { asRow } from '../../shared/utils/date';

@Component({
  selector: 'app-majorations',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, PageHeader, SoftCard, DataTable, SoftModal, SoftInput, SoftButton],
  template: `
    <page-header title="Majorations" subtitle="Taux NUIT / DIMANCHE / FERIE"></page-header>
    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()"
        [serverSide]="true" [total]="total()" [serverPage]="page()" [serverPageSize]="pageSize"
        [(searchTerm)]="tableSearch" (searchSubmit)="onTableSearch($event)"
        (pageChange)="onPageChange($event)">
        <ng-template #actions let-row>
          <soft-button *ngIf="canUpdate()" size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
        </ng-template>
      </data-table>
    </soft-card>
    <soft-modal [open]="modal()" title="Modifier majoration" (closed)="modal.set(false)">
      <soft-input label="Cotation" type="number" [(ngModel)]="form.cotation" name="cotation"></soft-input>
      <div footer>
        <soft-button variant="ghost" (click)="modal.set(false)">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
})
export class MajorationsPage implements OnInit {
  private readonly svc = inject(MajorationsService);
  private readonly toast = inject(ToastService);
  private readonly session = inject(SessionStore);

  readonly canUpdate = computed(() => this.session.hasPermission('PARAMS:MAJORATIONS:UPDATE', Droit.Parameters));

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly page = signal(1);
  pageSize = 50;
  readonly total = signal(0);
  readonly tableSearch = signal('');
  private requestSeq = 0;
  form: Partial<Majoration> = {};
  readonly columns: Column[] = [
    { key: 'mojoration', label: 'Type' },
    { key: 'cotation', label: 'Cotation' },
  ];

  ngOnInit(): void { this.loadPage(); }
  loadPage(): void {
    const seq = ++this.requestSeq;
    this.loading.set(true);
    this.svc.listPaged(this.page(), this.pageSize, this.tableSearch()).subscribe({
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
  openEdit(row: Record<string, unknown>): void { this.editId.set(row['id'] as number); this.form = { ...(row as unknown as Majoration) }; this.modal.set(true); }
  save(): void {
    this.saving.set(true);
    const id = this.editId()!;
    this.svc.update(id, { ...(this.form as Majoration), id }).subscribe({
      next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.loadPage(); },
      error: () => this.saving.set(false),
    });
  }
}
