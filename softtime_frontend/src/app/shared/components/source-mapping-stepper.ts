import {
  ChangeDetectionStrategy,
  Component,
  effect,
  inject,
  input,
  output,
  signal,
  untracked,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { FieldRole, SageDb, PointeuseDb, ConnectionTestResult, SourceEntityMapping } from '../../shared/models';
import {
  SageDatabasesService,
} from '../../shared/services/users.service';
import {
  DiscoveryService,
  FieldRolesService,
  PointeuseDatabasesService,
} from '../../shared/services/catalog.service';
import {
  SelectOption,
  SoftButton,
  SoftInput,
  SoftModal,
  SoftSelect,
  SoftTabs,
  SoftToggle,
  TabItem,
} from '../../shared/components';

interface EntityState {
  kind: string;
  table: string | null;
  fields: Record<string, string | null>;
}

const SAGE_ENTITIES = ['EMPLOYEE', 'AFFECTATION', 'DEPARTMENT', 'COMPANY_CALENDAR', 'CONSTANT', 'ABSENCE_EVENT', 'EMPLOYEE_EVENT'];
const POINTEUSE_ENTITIES = ['PUNCH_USER', 'PUNCH'];

const ENTITY_LABELS: Record<string, string> = {
  EMPLOYEE: 'Employés',
  AFFECTATION: 'Affectations',
  DEPARTMENT: 'Départements',
  COMPANY_CALENDAR: 'Calendrier / fériés',
  CONSTANT: 'Constantes',
  ABSENCE_EVENT: 'Événements absence',
  EMPLOYEE_EVENT: 'Événements employé',
  PUNCH_USER: 'Utilisateurs pointeuse',
  PUNCH: 'Pointages',
};

/**
 * Stepper partagé de configuration d'une source externe :
 * 1. Connexion & test, 2. Tables sources (une par entité), 3. Mapping colonnes (avec auto-mapping).
 * En mode STANDARD, seule l'étape 1 est présentée.
 */
@Component({
  selector: 'soft-source-mapping-stepper',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, SoftModal, SoftButton, SoftInput, SoftSelect, SoftToggle, SoftTabs],
  template: `
    <soft-modal [open]="open()" [title]="title()" size="lg" (closed)="closed.emit()">
      <ol class="stepper__head">
        @for (s of visibleSteps(); track s.n) {
          <li class="stepper__item" [class.is-active]="step() === s.n" [class.is-done]="step() > s.n">
            <span class="stepper__num">{{ s.n }}</span> {{ s.label }}
          </li>
        }
      </ol>

      @if (step() === 1) {
        <soft-tabs [tabs]="typeTabs" [activeTab]="typeBase()" (activeTabChange)="onTypeBaseChange($event)" />
        <div class="form-grid">
          <soft-input label="Serveur" [(ngModel)]="form.serveur" name="serveur" (ngModelChange)="onConnectionChange()" />
          <label class="toggle-row">
            <span>Authentification SQL</span>
            <soft-toggle [(ngModel)]="form.sqlAuth" name="sqlAuth" (ngModelChange)="onConnectionChange()" />
          </label>
          @if (form.sqlAuth ?? true) {
            <soft-input label="Login SQL" [(ngModel)]="form.login" name="login" (ngModelChange)="onConnectionChange()" />
            <soft-input label="Mot de passe" type="password" [(ngModel)]="form.password" name="password" (ngModelChange)="onConnectionChange()" />
          }
          @if (loadingBases()) {
            <p class="muted">Recherche des bases sur ce serveur...</p>
          } @else if (bases().length) {
            <soft-select label="Base de données" [required]="true" [options]="baseOptions()" placeholder="Sélectionner une base"
              [ngModel]="form.nomBd" name="nomBd" (ngModelChange)="onBaseChange($event)" />
          } @else {
            <div class="inline-row">
              <soft-input label="Nom de la base" [ngModel]="form.nomBd" name="nomBd" (ngModelChange)="onBaseChange($event)" />
              <soft-button variant="secondary" size="sm" (click)="loadBases()" [disabled]="!form.serveur">Lister les bases</soft-button>
            </div>
          }
          @if (systemType() === 'POINTEUSE') {
            <soft-input label="Type pointage" [(ngModel)]="form.typePointage" name="typePointage" />
            <label class="toggle-row"><span>Active</span><soft-toggle [(ngModel)]="form.active" name="active" /></label>
          }
        </div>
        <div class="test-row">
          <soft-button variant="secondary" size="sm" [loading]="testing()" [disabled]="!canTest()" (click)="testConnection()">Tester la connexion</soft-button>
          @if (testResult(); as r) {
            <span [class]="r.success ? 'test-ok' : 'test-ko'">{{ r.message }}</span>
          }
        </div>
      }

      @if (step() === 2) {
        <p class="section-title">Tables sources (une par entité)</p>
        @for (e of entities(); track e.kind) {
          <soft-select [label]="entityLabel(e.kind)" [required]="true" [options]="tableOptions()" placeholder="Sélectionner une table"
            [ngModel]="e.table" [name]="'tbl_' + e.kind" (ngModelChange)="setEntityTable(e.kind, $event)" />
        }
      }

      @if (step() === 3) {
        @for (e of entities(); track e.kind) {
          @if (e.table) {
            <p class="section-title">{{ entityLabel(e.kind) }} — {{ e.table }}</p>
            @if (loadingColumns()[e.kind]) {
              <p class="muted">Chargement des colonnes...</p>
            } @else {
              @for (r of rolesFor(e.kind); track r.code) {
                <soft-select [label]="roleLabel(r)" [required]="r.isRequired" [options]="columnOptions(e.kind)" placeholder="Aucune"
                  [ngModel]="e.fields[r.code]" [name]="'fld_' + r.code" (ngModelChange)="setField(e.kind, r.code, $event)" />
              }
            }
          }
        }
      }

      <div footer>
        <soft-button variant="ghost" (click)="closed.emit()">Annuler</soft-button>
        @if (step() > 1) {
          <soft-button variant="secondary" (click)="back()">Retour</soft-button>
        }
        @if (step() < lastStep()) {
          <soft-button (click)="next()" [disabled]="!canNext()">Suivant</soft-button>
        } @else {
          <soft-button [loading]="saving()" [disabled]="!canSave()" (click)="save()">Enregistrer</soft-button>
        }
      </div>
    </soft-modal>
  `,
  styles: [
    `
      .stepper__head { display: flex; gap: 18px; list-style: none; margin: 0 0 16px; padding: 0; }
      .stepper__item { display: flex; align-items: center; gap: 8px; color: var(--text-secondary, #6b7280); font-weight: 600; }
      .stepper__item.is-active { color: var(--primary-600, #1d4ed8); }
      .stepper__item.is-done { color: var(--soft-success, #16a34a); }
      .stepper__num { display: inline-flex; width: 24px; height: 24px; border-radius: 50%; align-items: center; justify-content: center;
        background: var(--color-border, #e5e7eb); color: #111827; font-size: 13px; }
      .stepper__item.is-active .stepper__num { background: var(--primary-600, #1d4ed8); color: #fff; }
      .form-grid { display: grid; gap: 14px; }
      .toggle-row { display: flex; align-items: center; justify-content: space-between; font-weight: 600; }
      .inline-row { display: flex; gap: 10px; align-items: flex-end; }
      .section-title { margin: 14px 0 8px; font-weight: 600; }
      .test-row { display: flex; align-items: center; gap: 12px; margin-top: 10px; }
      .test-ok { color: var(--soft-success, #16a34a); font-size: 13px; }
      .test-ko { color: var(--soft-danger, #dc2626); font-size: 13px; }
      .muted { margin: 0; }
      soft-select, soft-input { display: block; }
    `,
  ],
})
export class SourceMappingStepper {
  private readonly discovery = inject(DiscoveryService);
  private readonly fieldRoles = inject(FieldRolesService);
  private readonly sageSvc = inject(SageDatabasesService);
  private readonly pointeuseSvc = inject(PointeuseDatabasesService);

  readonly open = input(false);
  readonly systemType = input<'SAGE' | 'POINTEUSE'>('SAGE');
  readonly title = input('Configuration de la source');
  readonly initial = input<(Partial<SageDb> & Partial<PointeuseDb>) | null>(null);
  readonly saving = input(false);
  readonly closed = output<void>();
  readonly saved = output<Record<string, unknown>>();

  readonly typeTabs: TabItem[] = [
    { id: 'STANDARD', label: 'Base standard' },
    { id: 'AUTRE', label: 'Autre base' },
  ];

  readonly step = signal(1);
  readonly typeBase = signal<'STANDARD' | 'AUTRE'>('STANDARD');
  readonly testing = signal(false);
  readonly loadingBases = signal(false);
  readonly testResult = signal<ConnectionTestResult | null>(null);
  readonly roles = signal<FieldRole[]>([]);
  readonly entities = signal<EntityState[]>([]);
  readonly columnsByKind = signal<Record<string, string[]>>({});
  readonly loadingColumns = signal<Record<string, boolean>>({});

  private system: 'SAGE' | 'POINTEUSE' = 'SAGE';
  private loadedConnKey = '';
  private readonly basesSig = signal<string[]>([]);
  private readonly tablesSig = signal<string[]>([]);
  form: Partial<SageDb> & Partial<PointeuseDb> = { sqlAuth: true };

  constructor() {
    effect(() => {
      const open = this.open();
      const system = this.systemType();
      const init = this.initial();
      if (open) untracked(() => this.reset(system, init));
    });
  }

  bases(): string[] { return this.basesSig(); }
  baseOptions(): SelectOption[] { return this.basesSig().map((b) => ({ value: b, label: b })); }
  tableOptions(): SelectOption[] { return this.tablesSig().map((t) => ({ value: t, label: t })); }
  columnOptions(kind: string): SelectOption[] {
    return (this.columnsByKind()[kind] ?? []).map((c) => ({ value: c, label: c }));
  }
  rolesFor(kind: string): FieldRole[] { return this.roles().filter((r) => r.entityKind === kind); }
  entityLabel(kind: string): string { return ENTITY_LABELS[kind] ?? kind; }
  roleLabel(role: FieldRole): string { return `${role.label ?? role.code}${role.isRequired ? ' *' : ''}`; }

  visibleSteps(): { n: number; label: string }[] {
    const all = [
      { n: 1, label: 'Connexion' },
      { n: 2, label: 'Tables' },
      { n: 3, label: 'Colonnes' },
    ];
    return this.typeBase() === 'AUTRE' ? all : all.slice(0, 1);
  }

  lastStep(): number { return this.typeBase() === 'AUTRE' ? 3 : 1; }

  onTypeBaseChange(value: string): void {
    const t = value === 'AUTRE' ? 'AUTRE' : 'STANDARD';
    this.typeBase.set(t);
    this.form.typeBase = t;
    this.testResult.set(null);
    if (t === 'STANDARD') this.step.set(1);
  }

  onConnectionChange(): void {
    this.testResult.set(null);
    if (this.connKey() !== this.loadedConnKey) {
      this.loadedConnKey = this.connKey();
      this.clearMappings();
      this.basesSig.set([]);
      this.tablesSig.set([]);
    }
  }

  canTest(): boolean {
    return !!this.form.serveur && !!this.form.nomBd;
  }

  onBaseChange(base: string | null): void {
    this.form.nomBd = base;
    this.testResult.set(null);
    this.tablesSig.set([]);
    if (this.connKey() !== this.loadedConnKey) {
      this.loadedConnKey = this.connKey();
      this.clearMappings();
    }
    if (this.typeBase() === 'AUTRE' && base) this.loadTables();
  }

  /** Réinitialise les tables/colonnes sélectionnées (elles appartiennent à une autre connexion). */
  private clearMappings(): void {
    this.entities.update((list) => list.map((e) => ({ ...e, table: null, fields: {} })));
    this.columnsByKind.set({});
    this.loadingColumns.set({});
  }

  private connKey(): string {
    return `${this.form.serveur ?? ''}|${this.form.nomBd ?? ''}`;
  }

  loadBases(): void {
    if (!this.form.serveur) return;
    this.loadingBases.set(true);
    this.discovery.databases({ serveur: this.form.serveur, ...this.authPayload() }).subscribe({
      next: (list) => { this.basesSig.set(list); this.loadingBases.set(false); },
      error: () => this.loadingBases.set(false),
    });
  }

  loadTables(): void {
    if (!this.form.serveur || !this.form.nomBd) return;
    this.discovery.tables({ serveur: this.form.serveur, base: this.form.nomBd, ...this.authPayload() }).subscribe({
      next: (list) => this.tablesSig.set(list),
    });
  }

  setEntityTable(kind: string, table: string | null): void {
    this.entities.update((list) => list.map((e) => (e.kind === kind ? { ...e, table, fields: {} } : e)));
    if (table && this.form.serveur && this.form.nomBd) this.loadColumns(kind, table);
  }

  setField(kind: string, roleCode: string, column: string | null): void {
    this.entities.update((list) =>
      list.map((e) => (e.kind === kind ? { ...e, fields: { ...e.fields, [roleCode]: column } } : e)),
    );
  }

  testConnection(): void {
    this.testing.set(true);
    this.testResult.set(null);
    const dto = { ...this.form, typeBase: this.typeBase(), mappings: this.buildMappings() };
    const obs = this.system === 'SAGE' ? this.sageSvc.testConnection(dto) : this.pointeuseSvc.testConnection(dto);
    obs.subscribe({
      next: (r) => { this.testResult.set(r); this.testing.set(false); },
      error: () => this.testing.set(false),
    });
  }

  next(): void {
    const nextStep = this.step() + 1;
    this.step.set(nextStep);
    if (nextStep === 2 && this.tablesSig().length === 0) this.loadTables();
  }

  back(): void {
    if (this.step() > 1) this.step.set(this.step() - 1);
  }

  canNext(): boolean {
    if (this.step() === 1) return this.testResult()?.success === true;
    if (this.step() === 2) return this.entities().every((e) => !!e.table);
    return true;
  }

  canSave(): boolean {
    if (!this.canTest()) return false;
    if (this.typeBase() !== 'AUTRE') return true;
    return this.entities().every((e) => {
      if (!e.table) return false;
      return this.rolesFor(e.kind).filter((r) => r.isRequired).every((r) => !!e.fields[r.code]);
    });
  }

  save(): void {
    this.saved.emit({
      ...this.form,
      typeBase: this.typeBase(),
      mappings: this.typeBase() === 'AUTRE' ? this.buildMappings() : [],
    });
  }

  private reset(system: 'SAGE' | 'POINTEUSE', init: (Partial<SageDb> & Partial<PointeuseDb>) | null): void {
    this.system = system;
    this.form = init ? { ...init } : { sqlAuth: true };
    if (system === 'POINTEUSE' && this.form.active === undefined) this.form.active = true;
    this.loadedConnKey = this.connKey();
    this.typeBase.set((init?.typeBase === 'AUTRE' ? 'AUTRE' : 'STANDARD'));
    this.form.typeBase = this.typeBase();
    this.step.set(1);
    this.testResult.set(null);
    this.basesSig.set([]);
    this.tablesSig.set([]);
    this.columnsByKind.set({});
    this.loadingColumns.set({});
    this.entities.set(this.buildEntities(system, this.effectiveMappings(system, init)));
    this.fieldRoles.list(system).subscribe({
      next: (roles) => {
        this.roles.set(roles);
        this.entities.update((list) => list.map((e) => ({ ...e, fields: this.seedFields(e, roles) })));
        for (const e of this.entities()) if (e.table) this.loadColumns(e.kind, e.table);
      },
    });
    if (this.form.serveur) this.loadBases();
    if (this.typeBase() === 'AUTRE' && this.form.serveur && this.form.nomBd) this.loadTables();
  }

  /** Préremplit depuis les mappings persistés, sinon depuis les anciennes colonnes MAP_* (rétrocompatibilité). */
  private effectiveMappings(system: 'SAGE' | 'POINTEUSE', init: (Partial<SageDb> & Partial<PointeuseDb>) | null): SourceEntityMapping[] {
    const saved = init?.mappings ?? [];
    if (saved.length) return saved;
    return this.legacyMappings(system, init);
  }

  private legacyMappings(system: 'SAGE' | 'POINTEUSE', init: (Partial<SageDb> & Partial<PointeuseDb>) | null): SourceEntityMapping[] {
    const out: SourceEntityMapping[] = [];
    const add = (entityKind: string, table: string | null | undefined, pairs: [string, string | null | undefined][]) => {
      if (!table) return;
      const fields = pairs
        .filter(([, column]) => !!column)
        .map(([fieldRoleCode, column]) => ({ fieldRoleCode, sourceColumn: column as string }));
      out.push({ entityKind, sourceTable: table, fields });
    };
    if (system === 'SAGE') {
      add('EMPLOYEE', init?.mapTable, [
        ['SAGE_MATRICULE', init?.mapColMatricule],
        ['SAGE_NOM', init?.mapColNom],
        ['SAGE_PRENOM', init?.mapColPrenom],
        ['SAGE_BADGE', init?.mapColBadge],
      ]);
    } else {
      add('PUNCH_USER', init?.mapUserTable, [
        ['PTE_USER_ID', init?.mapUserColId],
        ['PTE_USER_BADGE', init?.mapUserColBadge],
        ['PTE_USER_SSN', init?.mapUserColSsn],
        ['PTE_USER_NOM', init?.mapUserColNom],
      ]);
      add('PUNCH', init?.mapPunchTable, [
        ['PTE_PUNCH_USER_ID', init?.mapPunchColUserId],
        ['PTE_PUNCH_DATETIME', init?.mapPunchColDateTime],
        ['PTE_PUNCH_TYPE', init?.mapPunchColType],
      ]);
    }
    return out;
  }

  private buildEntities(system: 'SAGE' | 'POINTEUSE', mappings: NonNullable<SageDb['mappings']>): EntityState[] {
    const kinds = system === 'SAGE' ? SAGE_ENTITIES : POINTEUSE_ENTITIES;
    return kinds.map((kind) => {
      const m = mappings.find((x) => x.entityKind === kind);
      const fields: Record<string, string | null> = {};
      for (const f of m?.fields ?? []) fields[f.fieldRoleCode] = f.sourceColumn ?? null;
      return { kind, table: m?.sourceTable ?? null, fields };
    });
  }

  private seedFields(entity: EntityState, roles: FieldRole[]): Record<string, string | null> {
    const fields = { ...entity.fields };
    for (const r of roles.filter((x) => x.entityKind === entity.kind)) {
      if (!(r.code in fields)) fields[r.code] = null;
    }
    return fields;
  }

  private loadColumns(kind: string, table: string): void {
    if (!this.form.serveur || !this.form.nomBd) return;
    this.loadingColumns.update((m) => ({ ...m, [kind]: true }));
    this.discovery.columns({ serveur: this.form.serveur, base: this.form.nomBd, table, ...this.authPayload() }).subscribe({
      next: (list) => {
        this.columnsByKind.update((m) => ({ ...m, [kind]: list }));
        this.loadingColumns.update((m) => ({ ...m, [kind]: false }));
        this.autoMap(kind, list);
      },
      error: () => this.loadingColumns.update((m) => ({ ...m, [kind]: false })),
    });
  }

  private autoMap(kind: string, columns: string[]): void {
    const roles = this.rolesFor(kind);
    this.entities.update((list) =>
      list.map((e) => {
        if (e.kind !== kind) return e;
        const fields = { ...e.fields };
        for (const r of roles) {
          if (fields[r.code]) continue;
          const match = this.matchColumn(r, columns);
          if (match) fields[r.code] = match;
        }
        return { ...e, fields };
      }),
    );
  }

  private matchColumn(role: FieldRole, columns: string[]): string | null {
    const tokens = (role.autoMappingPatterns ?? '').toLowerCase().split('|').map((t) => t.trim()).filter(Boolean);
    if (!tokens.length) return null;
    for (const t of tokens) {
      const exact = columns.find((c) => c.toLowerCase() === t);
      if (exact) return exact;
    }
    for (const t of tokens) {
      const partial = columns.find((c) => c.toLowerCase().includes(t));
      if (partial) return partial;
    }
    return null;
  }

  private buildMappings(): { entityKind: string; sourceTable: string | null; fields: { fieldRoleCode: string; sourceColumn: string | null }[] }[] {
    return this.entities()
      .filter((e) => !!e.table)
      .map((e) => ({
        entityKind: e.kind,
        sourceTable: e.table,
        fields: this.rolesFor(e.kind)
          .filter((r) => !!e.fields[r.code])
          .map((r) => ({ fieldRoleCode: r.code, sourceColumn: e.fields[r.code] ?? null })),
      }));
  }

  private authPayload(): { sqlAuth: boolean; login: string | null; password: string | null } {
    const sqlAuth = this.form.sqlAuth ?? true;
    return {
      sqlAuth,
      login: sqlAuth ? (this.form.login ?? null) : null,
      password: sqlAuth ? (this.form.password ?? null) : null,
    };
  }
}
