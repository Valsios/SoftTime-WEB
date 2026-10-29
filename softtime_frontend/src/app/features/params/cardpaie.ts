import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { SessionStore } from '../../core/session.store';
import { Droit } from '../../core/rights';
import { CardPaie, DepartementService, PageChangeEvent } from '../../shared/models';
import { CardPaieService, DepartementServiceService } from '../../shared/services/catalog.service';
import { ExcelExportService } from '../../shared/services/excel-export.service';
import { Column, DataTable, ImportTemplatesModal, PageHeader, SoftButton, SoftCard, SoftInput, SoftModal } from '../../shared/components';
import { asRow, fmtDate } from '../../shared/utils/date';

@Component({
  selector: 'app-cardpaie',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, PageHeader, SoftButton, SoftCard, DataTable, SoftModal, SoftInput, ImportTemplatesModal],
  template: `
    <page-header title="Correspondances paie" subtitle="Cartes SAGE ↔ pointeuse">
      <soft-button variant="secondary" (click)="exportExcel()">Export Excel</soft-button>
      <label class="file-btn">
        <input #csvFile type="file" accept=".csv,.txt" hidden (change)="onCsv($event)" />
        <soft-button variant="secondary" (click)="csvFile.click()">Import CSV</soft-button>
      </label>
      <soft-button variant="ghost" (click)="templatesOpen.set(true)">Modèles d'import</soft-button>
      <soft-button variant="secondary" [loading]="autoMapping()" [disabled]="autoMapping()" (click)="autoMap()">Auto-mapping</soft-button>
      <soft-button *ngIf="canCreate()" (click)="openCreate()">Ajouter</soft-button>
    </page-header>
    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()"
        [serverSide]="true" [total]="total()" [serverPage]="page()" [serverPageSize]="pageSize"
        (pageChange)="onPageChange($event)">
        <ng-template #actions let-row>
          <div class="soft-actions" style="justify-content: end">
            <soft-button *ngIf="canUpdate()" size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
            <soft-button *ngIf="canDelete()" size="sm" variant="danger" (click)="remove(row)">Supprimer</soft-button>
          </div>
        </ng-template>
      </data-table>
    </soft-card>
    <soft-modal [open]="modal()" [title]="editId() ? 'Modifier' : 'Nouvelle correspondance'" (closed)="modal.set(false)">
      <div class="form-grid">
        <soft-input label="Matricule SAGE" [(ngModel)]="form.sageMatricule" name="sageMatricule"></soft-input>
        <soft-input label="Nom SAGE" [(ngModel)]="form.sageNom" name="sageNom"></soft-input>
        <soft-input label="Prénom SAGE" [(ngModel)]="form.sagePrenom" name="sagePrenom"></soft-input>
        <soft-input label="N° pointeuse" [(ngModel)]="form.pointeuseNumero" name="pointeuseNumero"></soft-input>
        <soft-input label="Nom pointeuse" [(ngModel)]="form.pointeuseNom" name="pointeuseNom"></soft-input>
        <soft-input label="Branche" [(ngModel)]="form.branche" name="branche"></soft-input>
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
  styles: [`.form-grid { display: grid; gap: 14px; } .file-btn { cursor: pointer; }`],
})
export class CardPaiePage implements OnInit {
  private readonly svc = inject(CardPaieService);
  private readonly deptSvc = inject(DepartementServiceService);
  private readonly excel = inject(ExcelExportService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  private readonly session = inject(SessionStore);

  readonly canCreate = computed(() => this.session.hasPermission('PARAMS:CARDPAIE:CREATE', Droit.Parameters));
  readonly canUpdate = computed(() => this.session.hasPermission('PARAMS:CARDPAIE:UPDATE', Droit.Parameters));
  readonly canDelete = computed(() => this.session.hasPermission('PARAMS:CARDPAIE:DELETE', Droit.Parameters));

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly templatesOpen = signal(false);
  readonly importKeys = ['correspondences'];
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly page = signal(1);
  pageSize = 50;
  readonly total = signal(0);
  private requestSeq = 0;
  readonly autoMapping = signal(false);
  form: Partial<CardPaie> = {};

  readonly columns: Column[] = [
    { key: 'sageMatricule', label: 'Matricule' },
    { key: 'sageNom', label: 'Nom' },
    { key: 'sagePrenom', label: 'Prénom' },
    { key: 'departement', label: 'Département' },
    { key: 'service', label: 'Service' },
    { key: 'pointeuseNumero', label: 'Badge' },
    { key: 'date', label: 'Date', format: fmtDate },
  ];

  ngOnInit(): void { this.loadPage(); }

  loadPage(): void {
    const seq = ++this.requestSeq;
    this.loading.set(true);
    this.svc.listPaged(this.page(), this.pageSize).subscribe({
      next: (r) => {
        if (seq !== this.requestSeq) return;
        this.rows.set(r.items.map(asRow));
        this.total.set(r.total);
        this.page.set(r.page);
        this.loading.set(false);
        this.loadDepartements(r.items, seq);
      },
      error: () => { if (seq === this.requestSeq) this.loading.set(false); },
    });
  }

  onPageChange(event: PageChangeEvent): void {
    this.page.set(event.page);
    this.pageSize = event.pageSize;
    this.loadPage();
  }

  private loadDepartements(items: CardPaie[], seq: number): void {
    const matricules = items
      .map((i) => i.sageMatricule?.trim())
      .filter((m): m is string => !!m);
    if (matricules.length === 0) return;

    this.deptSvc.lookupBatch(matricules).subscribe({
      next: (results) => {
        if (seq !== this.requestSeq) return;
        const byMat = new Map<string, DepartementService>(results.map((r) => [r.matricule.trim(), r]));
        this.rows.update((rows) => rows.map((row) => {
          const mat = (row['sageMatricule'] as string | null)?.trim();
          const info = mat ? byMat.get(mat) : undefined;
          return { ...row, departement: info?.departement ?? null, service: info?.service ?? null };
        }));
      },
      error: () => undefined,
    });
  }

  openCreate(): void { this.editId.set(null); this.form = {}; this.modal.set(true); }
  openEdit(row: Record<string, unknown>): void { this.editId.set(row['id'] as number); this.form = { ...(row as unknown as CardPaie) }; this.modal.set(true); }
  save(): void {
    this.saving.set(true);
    const id = this.editId();
    const dto = this.form as CardPaie;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({ next: () => { this.saving.set(false); this.modal.set(false); this.toast.success('Enregistré.'); this.loadPage(); }, error: () => this.saving.set(false) });
  }
  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cette correspondance ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({ next: () => { this.toast.success('Supprimé.'); this.loadPage(); } });
  }
  autoMap(): void {
    this.autoMapping.set(true);
    this.svc.autoMap().subscribe({
      next: (r) => this.toast.success(r.message ?? `${r.added} correspondance(s) ajoutée(s).`),
      error: (e) => { this.autoMapping.set(false); this.toast.error(e?.error?.error || 'Synchronisation impossible.'); },
      complete: () => { this.autoMapping.set(false); this.loadPage(); },
    });
  }
  exportExcel(): void {
    this.svc.list().subscribe({
      next: (items) => {
        const matricules = items
          .map((i) => i.sageMatricule?.trim())
          .filter((m): m is string => !!m);
        if (matricules.length === 0) {
          this.excel.download('correspondances-paie', this.columns, items.map(asRow));
          return;
        }
        this.deptSvc.lookupBatch(matricules).subscribe({
          next: (results) => {
            const byMat = new Map(results.map((r) => [r.matricule.trim(), r]));
            const rows = items.map((item) => {
              const info = item.sageMatricule ? byMat.get(item.sageMatricule.trim()) : undefined;
              return { ...item, departement: info?.departement ?? null, service: info?.service ?? null };
            });
            this.excel.download('correspondances-paie', this.columns, rows.map(asRow));
          },
          error: () => this.excel.download('correspondances-paie', this.columns, items.map(asRow)),
        });
      },
    });
  }
  onCsv(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    this.svc.importCsv(file).subscribe({
      next: (r) => { this.toast.success(r.message || `${r.imported} importé(s).`); this.loadPage(); },
    });
    input.value = '';
  }
}