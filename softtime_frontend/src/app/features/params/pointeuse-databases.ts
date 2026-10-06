import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { PointeuseDb, ConnectionTestResult } from '../../shared/models';
import { PointeuseDatabasesService, DiscoveryService } from '../../shared/services/catalog.service';
import {
  Column,
  DataTable,
  PageHeader,
  SoftButton,
  SoftCard,
  SoftInput,
  SoftModal,
  SoftSelect,
  SoftTabs,
  SoftToggle,
  TabItem,
  SelectOption,
} from '../../shared/components';
import { asRow } from '../../shared/utils/date';
@Component({
  selector: 'app-pointeuse-databases',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    PageHeader,
    SoftButton,
    SoftCard,
    DataTable,
    SoftModal,
    SoftInput,
    SoftSelect,
    SoftTabs,
    SoftToggle,
  ],
  template: `
    <page-header title="Bases de pointage" subtitle="Connexions aux bases de pointage (pointeuse standard ou toute autre base)">
      <soft-button (click)="openCreate()">Ajouter</soft-button>
    </page-header>
    <soft-card>
      <data-table [columns]="columns" [rows]="rows()" [loading]="loading()">
        <ng-template #actions let-row>
          <div class="soft-actions">
            <soft-button size="sm" variant="ghost" (click)="openEdit(row)">Modifier</soft-button>
            <soft-button size="sm" variant="danger" (click)="remove(row)">Supprimer</soft-button>
          </div>
        </ng-template>
      </data-table>
    </soft-card>
    <soft-modal [open]="modal()" [title]="editId() ? 'Modifier' : 'Nouvelle base pointeuse'" (closed)="closeModal()">
      <form class="form-grid">
        <soft-tabs [tabs]="typeTabs" [activeTab]="form.typeBase || 'STANDARD'" (activeTabChange)="onTypeChange($event)" />
        <soft-input label="Serveur" [(ngModel)]="form.serveur" name="serveur" (ngModelChange)="resetDiscovery()"></soft-input>
        <label class="toggle-row">
          <span>Authentification SQL</span>
          <soft-toggle [(ngModel)]="form.sqlAuth" name="sqlAuth" (ngModelChange)="resetDiscovery()"></soft-toggle>
        </label>
        @if (form.sqlAuth ?? true) {
          <soft-input label="Login SQL" [(ngModel)]="form.login" name="login" (ngModelChange)="resetDiscovery()"></soft-input>
          <soft-input label="Mot de passe" type="password" [(ngModel)]="form.password" name="password" (ngModelChange)="resetDiscovery()"></soft-input>
        }
        @if (loadingBases()) {
          <p class="muted">Recherche des bases sur ce serveur...</p>
        } @else if (bases().length) {
          <soft-select
            label="Base de données"
            [required]="true"
            [options]="baseOptions()"
            placeholder="Sélectionner une base"
            [ngModel]="form.nomBd"
            name="nomBd"
            (ngModelChange)="onBaseChange($event)"
          ></soft-select>
        } @else {
          <div class="inline-row">
            <soft-input label="Nom de la base" [(ngModel)]="form.nomBd" name="nomBd"></soft-input>
            <soft-button variant="secondary" size="sm" (click)="loadBases()" [disabled]="!form.serveur">Lister les bases</soft-button>
          </div>
        }
        <soft-input label="Type pointage" [(ngModel)]="form.typePointage" name="typePointage"></soft-input>
        <label class="toggle-row"><span>Active</span><soft-toggle [(ngModel)]="form.active" name="active"></soft-toggle></label>
        @if (form.typeBase === 'AUTRE' && form.nomBd) {
          <p class="section-title">Table des utilisateurs</p>
          @if (loadingUserTables()) {
            <p class="muted">Chargement des tables...</p>
          } @else {
            <soft-select label="Table" [required]="true" [options]="tableOptions()" placeholder="Sélectionner une table" [ngModel]="form.mapUserTable" name="mapUserTable" (ngModelChange)="onUserTableChange($event)"></soft-select>
          }
          @if (form.mapUserTable) {
            @if (loadingUserColumns()) {
              <p class="muted">Chargement des colonnes...</p>
            } @else {
              <soft-select label="Colonne identifiant" [required]="true" [options]="userColumnOptions()" placeholder="Choisir" [(ngModel)]="form.mapUserColId" name="mapUserColId"></soft-select>
              <soft-select label="Colonne numéro de badge" [options]="userColumnOptions(true)" placeholder="Aucune" [(ngModel)]="form.mapUserColBadge" name="mapUserColBadge"></soft-select>
              <soft-select label="Colonne SSN / matricule" [options]="userColumnOptions(true)" placeholder="Aucune" [(ngModel)]="form.mapUserColSsn" name="mapUserColSsn"></soft-select>
              <soft-select label="Colonne nom" [options]="userColumnOptions(true)" placeholder="Aucune" [(ngModel)]="form.mapUserColNom" name="mapUserColNom"></soft-select>
            }
          }
          <p class="section-title">Table des pointages</p>
          @if (loadingPunchTables()) {
            <p class="muted">Chargement des tables...</p>
          } @else {
            <soft-select label="Table" [required]="true" [options]="tableOptions()" placeholder="Sélectionner une table" [ngModel]="form.mapPunchTable" name="mapPunchTable" (ngModelChange)="onPunchTableChange($event)"></soft-select>
          }
          @if (form.mapPunchTable) {
            @if (loadingPunchColumns()) {
              <p class="muted">Chargement des colonnes...</p>
            } @else {
              <soft-select label="Colonne identifiant utilisateur" [required]="true" [options]="punchColumnOptions()" placeholder="Choisir" [(ngModel)]="form.mapPunchColUserId" name="mapPunchColUserId"></soft-select>
              <soft-select label="Colonne date/heure" [required]="true" [options]="punchColumnOptions()" placeholder="Choisir" [(ngModel)]="form.mapPunchColDateTime" name="mapPunchColDateTime"></soft-select>
              <soft-select label="Colonne type (entrée/sortie)" [options]="punchColumnOptions(true)" placeholder="Aucune" [(ngModel)]="form.mapPunchColType" name="mapPunchColType"></soft-select>
            }
          }
        }
        <div class="test-row">
          <soft-button variant="secondary" size="sm" [loading]="testing()" (click)="testConnection()">Tester la connexion</soft-button>
          @if (testResult(); as r) {
            <span [class]="r.success ? 'test-ok' : 'test-ko'">{{ r.message }}</span>
          }
        </div>
      </form>
      <div footer>
        <soft-button variant="ghost" (click)="closeModal()">Annuler</soft-button>
        <soft-button [loading]="saving()" (click)="save()">Enregistrer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [
    `
      .form-grid { display: grid; gap: 14px; }
      .toggle-row { display: flex; justify-content: space-between; align-items: center; font-weight: 600; }
      .inline-row { display: flex; gap: 10px; align-items: flex-end; }
      .section-title { margin-top: 10px; font-weight: 600; }
      .test-row { display: flex; align-items: center; gap: 12px; margin-top: 10px; }
      .test-ok { color: var(--soft-success, #16a34a); font-size: 13px; }
      .test-ko { color: var(--soft-danger, #dc2626); font-size: 13px; }
      .muted { margin: 0; }
    `,
  ],
})
export class PointeuseDatabasesPage implements OnInit {
  private readonly svc = inject(PointeuseDatabasesService);
  private readonly discovery = inject(DiscoveryService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);
  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly modal = signal(false);
  readonly editId = signal<number | null>(null);
  readonly rows = signal<Record<string, unknown>[]>([]);
  readonly loadingBases = signal(false);
  readonly loadingUserTables = signal(false);
  readonly loadingUserColumns = signal(false);
  readonly loadingPunchTables = signal(false);
  readonly loadingPunchColumns = signal(false);
  readonly testing = signal(false);
  readonly testResult = signal<ConnectionTestResult | null>(null);
  readonly typeTabs: TabItem[] = [
    { id: 'STANDARD', label: 'Pointeuse standard' },
    { id: 'AUTRE', label: 'Autre base' },
  ];
  private bases_: string[] = [];
  private tables: string[] = [];
  private userColumns: string[] = [];
  private punchColumns: string[] = [];
  form: Partial<PointeuseDb> = {};
  readonly columns: Column[] = [
    { key: 'id', label: 'ID' },
    { key: 'serveur', label: 'Serveur' },
    { key: 'nomBd', label: 'Base' },
    { key: 'typeBase', label: 'Type' },
    { key: 'active', label: 'Active', format: (v) => (v ? 'Oui' : 'Non') },
  ];
  bases(): string[] { return this.bases_; }
  baseOptions(): SelectOption[] { return this.bases_.map((b) => ({ value: b, label: b })); }
  tableOptions(): SelectOption[] { return this.tables.map((t) => ({ value: t, label: t })); }
  userColumnOptions(withNone = false): SelectOption[] {
    const opts = this.userColumns.map((c) => ({ value: c, label: c }));
    return withNone ? [{ value: null, label: 'Aucune' }, ...opts] : opts;
  }
  punchColumnOptions(withNone = false): SelectOption[] {
    const opts = this.punchColumns.map((c) => ({ value: c, label: c }));
    return withNone ? [{ value: null, label: 'Aucune' }, ...opts] : opts;
  }
  ngOnInit(): void { this.load(); }
  load(): void {
    this.loading.set(true);
    this.svc.list().subscribe({
      next: (items) => { this.rows.set(items.map(asRow)); this.loading.set(false); },
      error: () => this.loading.set(false),
    });
  }
  openCreate(): void {
    this.editId.set(null);
    this.form = { sqlAuth: true, active: true, typeBase: 'STANDARD' };
    this.resetDiscoveryState();
    this.modal.set(true);
  }
  openEdit(row: Record<string, unknown>): void {
    this.editId.set(row['id'] as number);
    this.form = { ...(row as unknown as PointeuseDb) };
    this.resetDiscoveryState();
    if (this.form.serveur) this.loadBases();
    this.modal.set(true);
  }
  closeModal(): void { this.modal.set(false); }
  onTypeChange(t: string): void {
    this.form.typeBase = t;
    if (t === 'STANDARD') {
      this.form.mapUserTable = null;
      this.form.mapUserColId = null;
      this.form.mapUserColBadge = null;
      this.form.mapUserColSsn = null;
      this.form.mapUserColNom = null;
      this.form.mapPunchTable = null;
      this.form.mapPunchColUserId = null;
      this.form.mapPunchColDateTime = null;
      this.form.mapPunchColType = null;
    }
    this.testResult.set(null);
  }
  private authPayload() {
    const sqlAuth = this.form.sqlAuth ?? true;
    return { sqlAuth, login: sqlAuth ? (this.form.login ?? null) : null, password: sqlAuth ? (this.form.password ?? null) : null };
  }
  private resetDiscoveryState(): void {
    this.bases_ = [];
    this.tables = [];
    this.userColumns = [];
    this.punchColumns = [];
    this.testResult.set(null);
  }
  resetDiscovery(): void { this.resetDiscoveryState(); }
  loadBases(): void {
    if (!this.form.serveur) return;
    this.loadingBases.set(true);
    this.discovery.databases({ serveur: this.form.serveur, ...this.authPayload() }).subscribe({
      next: (list) => { this.bases_ = list; this.loadingBases.set(false); },
      error: () => { this.loadingBases.set(false); this.toast.error('Connexion au serveur impossible — vérifie le login/mot de passe.'); },
    });
  }
  onBaseChange(base: string | null): void {
    this.form.nomBd = base;
    this.tables = [];
    this.userColumns = [];
    this.punchColumns = [];
    this.testResult.set(null);
    if (this.form.typeBase === 'AUTRE' && base && this.form.serveur) this.loadTables();
  }
  private loadTables(): void {
    if (!this.form.serveur || !this.form.nomBd) return;
    this.loadingUserTables.set(true);
    this.loadingPunchTables.set(true);
    this.discovery.tables({ serveur: this.form.serveur, base: this.form.nomBd, ...this.authPayload() }).subscribe({
      next: (list) => { this.tables = list; this.loadingUserTables.set(false); this.loadingPunchTables.set(false); },
      error: () => {
        this.loadingUserTables.set(false);
        this.loadingPunchTables.set(false);
        this.toast.error('Impossible de lister les tables de cette base.');
      },
    });
  }
  onUserTableChange(table: string | null): void {
    this.form.mapUserTable = table;
    this.userColumns = [];
    this.form.mapUserColId = null;
    this.form.mapUserColBadge = null;
    this.form.mapUserColSsn = null;
    this.form.mapUserColNom = null;
    if (!table || !this.form.serveur || !this.form.nomBd) return;
    this.loadingUserColumns.set(true);
    this.discovery.columns({ serveur: this.form.serveur, base: this.form.nomBd, table, ...this.authPayload() }).subscribe({
      next: (list) => { this.userColumns = list; this.loadingUserColumns.set(false); },
      error: () => { this.loadingUserColumns.set(false); this.toast.error('Impossible de lister les colonnes de cette table.'); },
    });
  }
  onPunchTableChange(table: string | null): void {
    this.form.mapPunchTable = table;
    this.punchColumns = [];
    this.form.mapPunchColUserId = null;
    this.form.mapPunchColDateTime = null;
    this.form.mapPunchColType = null;
    if (!table || !this.form.serveur || !this.form.nomBd) return;
    this.loadingPunchColumns.set(true);
    this.discovery.columns({ serveur: this.form.serveur, base: this.form.nomBd, table, ...this.authPayload() }).subscribe({
      next: (list) => { this.punchColumns = list; this.loadingPunchColumns.set(false); },
      error: () => { this.loadingPunchColumns.set(false); this.toast.error('Impossible de lister les colonnes de cette table.'); },
    });
  }
  testConnection(): void {
    this.testing.set(true);
    this.testResult.set(null);
    this.svc.testConnection({ ...this.form }).subscribe({
      next: (r) => { this.testResult.set(r); this.testing.set(false); },
      error: () => { this.testing.set(false); this.toast.error('Test de connexion impossible.'); },
    });
  }
  save(): void {
    this.saving.set(true);
    const id = this.editId();
    const dto = { ...(this.form as PointeuseDb), typeBase: this.form.typeBase || 'STANDARD' };
    const isNew = !id;
    const req = id ? this.svc.update(id, { ...dto, id }) : this.svc.create({ ...dto, id: 0 });
    req.subscribe({
      next: () => {
        this.saving.set(false);
        this.modal.set(false);
        this.toast.success('Enregistré.');
        if (isNew) {
          window.location.reload();
        } else {
          this.load();
        }
      },
      error: (e) => { this.saving.set(false); this.toast.error(e?.error?.title || e?.error || 'Enregistrement impossible.'); },
    });
  }
  async remove(row: Record<string, unknown>): Promise<void> {
    if (!(await this.confirm.ask('Supprimer cette base pointeuse ?'))) return;
    this.svc.remove(row['id'] as number).subscribe({ next: () => { this.toast.success('Supprimé.'); this.load(); } });
  }
}