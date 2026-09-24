import { ChangeDetectionStrategy, Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { SourceConfig } from '../../shared/models';
import { SourceConfigService } from '../../shared/services/catalog.service';
import { PageHeader, SoftButton, SoftCard, SoftInput, SoftSelect, SoftTabs, SoftToggle, TabItem, SelectOption } from '../../shared/components';

@Component({
  selector: 'app-source-config',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, PageHeader, SoftButton, SoftCard, SoftInput, SoftSelect, SoftTabs, SoftToggle],
  template: `
    <page-header title="Source de données" subtitle="D'où proviennent matricule, département et service">
      <soft-button [loading]="saving()" (click)="save()" [disabled]="!canSave()">Enregistrer</soft-button>
    </page-header>

    <soft-card padding="lg">
      @if (loading()) {
        <div class="soft-spinner-lg"></div>
      } @else {
        <soft-tabs [tabs]="modeTabs" [activeTab]="mode" (activeTabChange)="onModeChange($event)" />

        @if (mode === 'SAGE') {
          <p class="muted">
            Les données proviennent automatiquement de SAGE. Aucune configuration supplémentaire n'est nécessaire.
          </p>
        } @else {
          <p class="section-title">1. Authentification</p>
          <label class="toggle-row">
            <div>
              <strong>Authentification SQL</strong>
              <p class="muted">
                Activé : connexion avec login + mot de passe SQL.
                Désactivé : authentification Windows (le serveur applicatif utilise son propre compte).
              </p>
            </div>
            <soft-toggle [(ngModel)]="form.extSqlAuth" name="extSqlAuth" (ngModelChange)="onAuthChange()"></soft-toggle>
          </label>

          @if (form.extSqlAuth ?? true) {
            <div class="form-grid">
              <soft-input label="Login" [(ngModel)]="form.extLogin" name="extLogin" (ngModelChange)="resetFromServer()"></soft-input>
              <soft-input label="Mot de passe" type="password" [(ngModel)]="form.extPassword" name="extPassword" placeholder="Laisser vide pour ne pas changer" (ngModelChange)="resetFromServer()"></soft-input>
            </div>
          }

          <p class="section-title">2. Serveur</p>
          @if (loadingServers()) {
            <p class="muted">Recherche des serveurs...</p>
          } @else {
            <div class="form-grid">
              <soft-select
                label="Serveur"
                [required]="true"
                [options]="serverOptions()"
                placeholder="Sélectionner un serveur"
                [ngModel]="form.extServeur"
                (ngModelChange)="onServerChange($event)"
              ></soft-select>
              <soft-button variant="secondary" size="sm" (click)="loadServers()">Rafraîchir la liste</soft-button>
            </div>
          }

          @if (form.extServeur) {
            <p class="section-title">3. Base de données</p>
            @if (loadingDatabases()) {
              <p class="muted">Chargement des bases...</p>
            } @else {
              <div class="form-grid">
                <soft-select
                  label="Base de données"
                  [required]="true"
                  [options]="databaseOptions()"
                  placeholder="Sélectionner une base"
                  [ngModel]="form.extBase"
                  (ngModelChange)="onDatabaseChange($event)"
                ></soft-select>
              </div>
            }
          }

          @if (form.extBase && !confirmed()) {
            <div class="confirm-row">
              <soft-button variant="primary" (click)="confirmDatabase()" [loading]="loadingTables()">Confirmer</soft-button>
            </div>
          }

          @if (confirmed()) {
            <p class="section-title">4. Table</p>
            @if (loadingTables()) {
              <p class="muted">Chargement des tables...</p>
            } @else {
              <div class="form-grid">
                <soft-select
                  label="Nom de la table"
                  [required]="true"
                  [options]="tableOptions()"
                  placeholder="Sélectionner une table"
                  [ngModel]="form.tableName"
                  (ngModelChange)="onTableChange($event)"
                ></soft-select>
              </div>
            }
          }

          @if (form.tableName) {
            <p class="section-title">5. Colonnes</p>
            @if (loadingColumns()) {
              <p class="muted">Chargement des colonnes...</p>
            } @else {
              <div class="form-grid">
                <soft-select label="Colonne matricule" [required]="true" [options]="columnOptions()" placeholder="Sélectionner une colonne" [(ngModel)]="form.colMatricule" name="colMatricule"></soft-select>
                <soft-select label="Colonne département" [options]="columnOptions(true)" placeholder="Aucune" [(ngModel)]="form.colDepartement" name="colDepartement"></soft-select>
                <soft-select label="Colonne service" [options]="columnOptions(true)" placeholder="Aucune" [(ngModel)]="form.colService" name="colService"></soft-select>
                <soft-select label="Colonne code département" [options]="columnOptions(true)" placeholder="Aucune" [(ngModel)]="form.colCodeDepartement" name="colCodeDepartement"></soft-select>
              </div>
            }
          }
        }
      }
    </soft-card>
  `,
  styles: [
    `.form-grid { display: grid; gap: 14px; margin-top: 20px; max-width: 420px; }
     .confirm-row { margin-top: 24px; }
     .muted { margin-top: 20px; }
     .section-title { margin-top: 32px; font-weight: 600; }
     .toggle-row { display:flex; align-items:center; justify-content:space-between; gap:16px; margin-top: 20px; max-width: 420px; }
     .toggle-row p { margin:4px 0 0; font-size:13px; }`,
  ],
})
export class SourceConfigPage implements OnInit {
  private readonly svc = inject(SourceConfigService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly loadingServers = signal(false);
  readonly loadingDatabases = signal(false);
  readonly loadingTables = signal(false);
  readonly loadingColumns = signal(false);
  readonly confirmed = signal(false);

  readonly modeTabs: TabItem[] = [
    { id: 'SAGE', label: 'SAGE' },
    { id: 'AUTRE', label: 'Autre base' },
  ];

  private servers: string[] = [];
  private databases: string[] = [];
  private tables: string[] = [];
  private columns: string[] = [];

  mode = 'SAGE';
  id = 0;
  form: Partial<SourceConfig> = {};

  serverOptions(): SelectOption[] {
    return this.servers.map((s) => ({ value: s, label: s }));
  }
  databaseOptions(): SelectOption[] {
    return this.databases.map((d) => ({ value: d, label: d }));
  }
  tableOptions(): SelectOption[] {
    return this.tables.map((t) => ({ value: t, label: t }));
  }
  columnOptions(withNone = false): SelectOption[] {
    const opts = this.columns.map((c) => ({ value: c, label: c }));
    return withNone ? [{ value: null, label: 'Aucune' }, ...opts] : opts;
  }

  canSave(): boolean {
    if (this.mode === 'SAGE') return true;
    return !!(this.form.tableName && this.form.colMatricule);
  }

  ngOnInit(): void {
    this.svc.get().subscribe({
      next: (c) => {
        this.id = c.id;
        this.mode = c.mode;
        this.form = { ...c, extPassword: null, extSqlAuth: c.extSqlAuth ?? true };
        this.loading.set(false);
        if (this.mode === 'AUTRE') this.restoreAutreState();
      },
      error: () => this.loading.set(false),
    });
  }

  private restoreAutreState(): void {
    this.loadServers();
    const serveur = this.form.extServeur;
    const base = this.form.extBase;
    const table = this.form.tableName;
    if (!serveur) return;

    this.loadingDatabases.set(true);
    this.svc.discoverDatabases({ serveur, ...this.authPayload() }).subscribe({
      next: (list) => {
        this.databases = list;
        this.loadingDatabases.set(false);
        if (!base) return;

        this.loadingTables.set(true);
        this.svc.discoverTables({ serveur, base, ...this.authPayload() }).subscribe({
          next: (tlist) => {
            this.tables = tlist;
            this.confirmed.set(true);
            this.loadingTables.set(false);
            if (!table) return;

            this.loadingColumns.set(true);
            this.svc.discoverColumns({ serveur, base, table, ...this.authPayload() }).subscribe({
              next: (clist) => {
                this.columns = clist;
                this.loadingColumns.set(false);
              },
              error: () => {
                this.loadingColumns.set(false);
                this.toast.error('Impossible de recharger les colonnes.');
              },
            });
          },
          error: () => {
            this.loadingTables.set(false);
            this.toast.error('Impossible de recharger les tables.');
          },
        });
      },
      error: () => {
        this.loadingDatabases.set(false);
        this.toast.error('Impossible de recharger les bases (vérifie login/mot de passe).');
      },
    });
  }

  onModeChange(newMode: string): void {
    this.mode = newMode;
    if (newMode === 'AUTRE' && this.servers.length === 0) this.loadServers();
  }

  onAuthChange(): void {
    this.resetFromServer();
  }

  private authPayload() {
    const sqlAuth = this.form.extSqlAuth ?? true;
    return {
      sqlAuth,
      login: sqlAuth ? (this.form.extLogin ?? null) : null,
      password: sqlAuth ? (this.form.extPassword ?? null) : null,
    };
  }

  loadServers(): void {
    this.loadingServers.set(true);
    this.svc.discoverServers().subscribe({
      next: (list) => {
        this.servers = list;
        this.loadingServers.set(false);
      },
      error: () => {
        this.loadingServers.set(false);
        this.toast.error('Impossible de récupérer la liste des serveurs.');
      },
    });
  }

  resetFromServer(): void {
    this.databases = [];
    this.tables = [];
    this.columns = [];
    this.confirmed.set(false);
    this.form.extBase = null;
    this.form.tableName = null;
    this.form.colMatricule = null;
    this.form.colDepartement = null;
    this.form.colService = null;
    this.form.colCodeDepartement = null;
  }

  onServerChange(serveur: string | null): void {
    this.form.extServeur = serveur;
    this.resetFromServer();
    if (!serveur) return;
    this.loadingDatabases.set(true);
    this.svc.discoverDatabases({ serveur, ...this.authPayload() }).subscribe({
      next: (list) => {
        this.databases = list;
        this.loadingDatabases.set(false);
      },
      error: () => {
        this.loadingDatabases.set(false);
        this.toast.error('Connexion au serveur impossible — vérifie le login/mot de passe.');
      },
    });
  }

  onDatabaseChange(base: string | null): void {
    this.form.extBase = base;
    this.tables = [];
    this.columns = [];
    this.confirmed.set(false);
    this.form.tableName = null;
    this.form.colMatricule = null;
    this.form.colDepartement = null;
    this.form.colService = null;
    this.form.colCodeDepartement = null;
  }

  confirmDatabase(): void {
    if (!this.form.extServeur || !this.form.extBase) return;
    this.loadingTables.set(true);
    this.svc.discoverTables({ serveur: this.form.extServeur, base: this.form.extBase, ...this.authPayload() }).subscribe({
      next: (list) => {
        this.tables = list;
        this.confirmed.set(true);
        this.loadingTables.set(false);
      },
      error: () => {
        this.loadingTables.set(false);
        this.toast.error('Impossible de lister les tables de cette base.');
      },
    });
  }

  onTableChange(table: string | null): void {
    this.form.tableName = table;
    this.columns = [];
    this.form.colMatricule = null;
    this.form.colDepartement = null;
    this.form.colService = null;
    this.form.colCodeDepartement = null;
    if (!table || !this.form.extServeur || !this.form.extBase) return;
    this.loadingColumns.set(true);
    this.svc.discoverColumns({ serveur: this.form.extServeur, base: this.form.extBase, table, ...this.authPayload() }).subscribe({
      next: (list) => {
        this.columns = list;
        this.loadingColumns.set(false);
      },
      error: () => {
        this.loadingColumns.set(false);
        this.toast.error('Impossible de lister les colonnes de cette table.');
      },
    });
  }

  save(): void {
    this.saving.set(true);
    const dto: SourceConfig = {
      id: this.id,
      mode: this.mode,
      tableName: this.mode === 'AUTRE' ? (this.form.tableName ?? null) : null,
      colMatricule: this.mode === 'AUTRE' ? (this.form.colMatricule ?? null) : null,
      colDepartement: this.mode === 'AUTRE' ? (this.form.colDepartement ?? null) : null,
      colService: this.mode === 'AUTRE' ? (this.form.colService ?? null) : null,
      colCodeDepartement: this.mode === 'AUTRE' ? (this.form.colCodeDepartement ?? null) : null,
      extServeur: this.mode === 'AUTRE' ? (this.form.extServeur ?? null) : null,
      extBase: this.mode === 'AUTRE' ? (this.form.extBase ?? null) : null,
      extLogin: this.mode === 'AUTRE' ? (this.form.extLogin ?? null) : null,
      extPassword: this.mode === 'AUTRE' ? (this.form.extPassword ?? null) : null,
      extSqlAuth: this.mode === 'AUTRE' ? (this.form.extSqlAuth ?? true) : true,
    };
    this.svc.update(dto).subscribe({
      next: (c) => {
        this.id = c.id;
        this.form = { ...this.form, extPassword: null, extSqlAuth: c.extSqlAuth ?? true };
        this.saving.set(false);
        this.toast.success('Configuration enregistrée.');
      },
      error: () => this.saving.set(false),
    });
  }
}