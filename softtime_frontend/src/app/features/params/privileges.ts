import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { SessionStore } from '../../core/session.store';
import { PointeuseDb, SageDb, User } from '../../shared/models';
import { PointeuseDatabasesService } from '../../shared/services/catalog.service';
import { PrivilegesService, SageDatabasesService, UsersService } from '../../shared/services/users.service';
import { AuthService } from '../../shared/services/auth.service';
import { PageHeader, SoftButton, SoftCard, SoftSelect } from '../../shared/components';
import { Icon } from '../../shared/components/icon';

interface FeatureItem {
  code: string;
  label: string;
  canCreate?: boolean;
  canUpdate?: boolean;
  canDelete?: boolean;
  desc?: string;
}

interface FeatureCategory {
  key: string;
  title: string;
  icon: string;
  items: FeatureItem[];
}

@Component({
  selector: 'app-privileges',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, FormsModule, PageHeader, SoftButton, SoftCard, SoftSelect, Icon],
  template: `
    <page-header title="Privilèges" subtitle="Permissions détaillées par utilisateur et par fonctionnalité">
      <div class="header-actions">
        <soft-button [loading]="saving()" (click)="save()" [disabled]="!selectedUserId">
          Enregistrer les modifications
        </soft-button>
      </div>
    </page-header>

    <!-- Sélecteur d'utilisateur -->
    <soft-card padding="lg" class="user-select-card">
      <div class="user-selector-bar">
        <div class="user-selector-field">
          <soft-select
            label="Sélectionner un utilisateur"
            [(ngModel)]="selectedUserId"
            name="userId"
            [options]="userOptions()"
            (ngModelChange)="onUserChange($event)"
          ></soft-select>
        </div>

        <div class="user-status-badges" *ngIf="selectedUser() as u">
          <span class="user-role-badge">
            <app-icon name="shield" [size]="14"></app-icon>
            Rôle : {{ getRoleLabel(u.roleId) }}
          </span>
          <span class="user-status-tag" [class.is-custom]="isCustomized()">
            {{ isCustomized() ? 'Permissions personnalisées' : 'Permissions par défaut (Rôle)' }}
          </span>
          <span class="user-status-tag is-admin-shield" *ngIf="isTargetAdmin()">
            Bases et gestion verrouillées
          </span>
        </div>

        <div class="bulk-actions">
          <button type="button" class="action-link-btn" (click)="toggleAll(true)">
            Tout cocher
          </button>
          <span class="sep">|</span>
          <button type="button" class="action-link-btn" (click)="toggleAll(false)">
            Tout décocher
          </button>
        </div>
      </div>
    </soft-card>

    <div class="permissions-container" *ngIf="selectedUserId">
      <!-- Section 1 : Bases RH / Paie -->
      <soft-card padding="lg" class="perm-card" [class.is-collapsed]="isCollapsed('SAGE')">
        <div class="perm-card-header">
          <div class="perm-title-group">
            <app-icon name="server" [size]="20"></app-icon>
            <h3>Bases RH / Paie</h3>
            <span class="count-badge">{{ countActiveCategory('SAGE') }} / {{ totalSagePerms() }}</span>
          </div>
          <div class="card-bulk-actions">
            <label class="action-check header-action-check">
              <input
                type="checkbox"
                [checked]="has('SAGE:CREATE')"
                [disabled]="isMandatory('SAGE:CREATE')"
                (change)="toggle('SAGE:CREATE', $event)"
              />
              <span>Ajouter une base</span>
            </label>
            <button type="button" class="btn-xs" (click)="toggleGroup('SAGE', true)">Tout cocher</button>
            <button type="button" class="btn-xs" (click)="toggleGroup('SAGE', false)" [disabled]="isTargetAdmin()">Tout décocher</button>
            <button
              type="button"
              class="btn-xs btn-collapse"
              [title]="isCollapsed('SAGE') ? 'Déplier' : 'Replier'"
              (click)="toggleCollapse('SAGE')"
            >
              <app-icon [name]="isCollapsed('SAGE') ? 'chevron-down' : 'chevron-up'" [size]="14"></app-icon>
            </button>
          </div>
        </div>

        @if (!isCollapsed('SAGE')) {
          <p class="perm-section-desc">Permissions d'accès et d'actions (Consulter, Modifier, Supprimer) par base RH configurée.</p>

          @if (!sageDbs().length) {
            <div class="empty-hint">Aucune base SAGE configurée.</div>
          } @else {
            <div class="db-perm-grid">
              @for (db of sageDbs(); track db.id) {
                <div class="db-perm-row">
                  <div class="db-perm-name">
                    <strong>{{ db.nomBd }}</strong>
                    <small>{{ db.serveur }}</small>
                  </div>
                  <div class="db-actions-toggles">
                    <label class="action-check">
                      <input
                        type="checkbox"
                        [checked]="has('SAGE:' + db.nomBd + ':READ')"
                        [disabled]="isMandatory('SAGE:' + db.nomBd + ':READ')"
                        (change)="toggle('SAGE:' + db.nomBd + ':READ', $event)"
                      />
                      <span>Consulter</span>
                    </label>
                    <label class="action-check">
                      <input
                        type="checkbox"
                        [checked]="has('SAGE:' + db.nomBd + ':UPDATE')"
                        [disabled]="isMandatory('SAGE:' + db.nomBd + ':UPDATE')"
                        (change)="toggle('SAGE:' + db.nomBd + ':UPDATE', $event)"
                      />
                      <span>Modifier</span>
                    </label>
                    <label class="action-check">
                      <input
                        type="checkbox"
                        [checked]="has('SAGE:' + db.nomBd + ':DELETE')"
                        [disabled]="isMandatory('SAGE:' + db.nomBd + ':DELETE')"
                        (change)="toggle('SAGE:' + db.nomBd + ':DELETE', $event)"
                      />
                      <span>Supprimer</span>
                    </label>
                  </div>
                </div>
              }
            </div>
          }
        }
      </soft-card>

      <!-- Section 2 : Bases de pointage -->
      <soft-card padding="lg" class="perm-card" [class.is-collapsed]="isCollapsed('POINTEUSE')">
        <div class="perm-card-header">
          <div class="perm-title-group">
            <app-icon name="clock" [size]="20"></app-icon>
            <h3>Bases de pointage</h3>
            <span class="count-badge">{{ countActiveCategory('POINTEUSE') }} / {{ totalPointeusePerms() }}</span>
          </div>
          <div class="card-bulk-actions">
            <label class="action-check header-action-check">
              <input
                type="checkbox"
                [checked]="has('POINTEUSE:CREATE')"
                [disabled]="isMandatory('POINTEUSE:CREATE')"
                (change)="toggle('POINTEUSE:CREATE', $event)"
              />
              <span>Ajouter une base</span>
            </label>
            <button type="button" class="btn-xs" (click)="toggleGroup('POINTEUSE', true)">Tout cocher</button>
            <button type="button" class="btn-xs" (click)="toggleGroup('POINTEUSE', false)" [disabled]="isTargetAdmin()">Tout décocher</button>
            <button
              type="button"
              class="btn-xs btn-collapse"
              [title]="isCollapsed('POINTEUSE') ? 'Déplier' : 'Replier'"
              (click)="toggleCollapse('POINTEUSE')"
            >
              <app-icon [name]="isCollapsed('POINTEUSE') ? 'chevron-down' : 'chevron-up'" [size]="14"></app-icon>
            </button>
          </div>
        </div>

        @if (!isCollapsed('POINTEUSE')) {
          <p class="perm-section-desc">Permissions d'accès et d'actions (Consulter, Modifier, Supprimer) par pointeuse configurée.</p>

          @if (!pointeuseDbs().length) {
            <div class="empty-hint">Aucune base pointeuse configurée.</div>
          } @else {
            <div class="db-perm-grid">
              @for (db of pointeuseDbs(); track db.id) {
                <div class="db-perm-row">
                  <div class="db-perm-name">
                    <strong>{{ db.nomBd }}</strong>
                    <small>{{ db.serveur }}</small>
                  </div>
                  <div class="db-actions-toggles">
                    <label class="action-check">
                      <input
                        type="checkbox"
                        [checked]="has('POINTEUSE:' + db.nomBd + ':READ')"
                        [disabled]="isMandatory('POINTEUSE:' + db.nomBd + ':READ')"
                        (change)="toggle('POINTEUSE:' + db.nomBd + ':READ', $event)"
                      />
                      <span>Consulter</span>
                    </label>
                    <label class="action-check">
                      <input
                        type="checkbox"
                        [checked]="has('POINTEUSE:' + db.nomBd + ':UPDATE')"
                        [disabled]="isMandatory('POINTEUSE:' + db.nomBd + ':UPDATE')"
                        (change)="toggle('POINTEUSE:' + db.nomBd + ':UPDATE', $event)"
                      />
                      <span>Modifier</span>
                    </label>
                    <label class="action-check">
                      <input
                        type="checkbox"
                        [checked]="has('POINTEUSE:' + db.nomBd + ':DELETE')"
                        [disabled]="isMandatory('POINTEUSE:' + db.nomBd + ':DELETE')"
                        (change)="toggle('POINTEUSE:' + db.nomBd + ':DELETE', $event)"
                      />
                      <span>Supprimer</span>
                    </label>
                  </div>
                </div>
              }
            </div>
          }
        }
      </soft-card>

      <!-- Sections 3 à 7 : Catégories fonctionnelles -->
      @for (cat of featureCategories; track cat.key) {
        <soft-card padding="lg" class="perm-card" [class.is-collapsed]="isCollapsed(cat.key)">
          <div class="perm-card-header">
            <div class="perm-title-group">
              <app-icon [name]="cat.icon" [size]="20"></app-icon>
              <h3>{{ cat.title }}</h3>
              <span class="count-badge">{{ countActiveCategory(cat.key) }} / {{ totalCategoryPerms(cat) }}</span>
            </div>
            <div class="card-bulk-actions">
              <button type="button" class="btn-xs" (click)="toggleGroup(cat.key, true)">Tout cocher</button>
              <button type="button" class="btn-xs" (click)="toggleGroup(cat.key, false)">Tout décocher</button>
              <button
                type="button"
                class="btn-xs btn-collapse"
                [title]="isCollapsed(cat.key) ? 'Déplier' : 'Replier'"
                (click)="toggleCollapse(cat.key)"
              >
                <app-icon [name]="isCollapsed(cat.key) ? 'chevron-down' : 'chevron-up'" [size]="14"></app-icon>
              </button>
            </div>
          </div>

          @if (!isCollapsed(cat.key)) {
            <div class="db-perm-grid">
              @for (item of cat.items; track item.code) {
                <div class="db-perm-row">
                  <label class="action-check item-access-check">
                    <input
                      type="checkbox"
                      [checked]="has(item.code)"
                      [disabled]="isMandatory(item.code)"
                      (change)="toggle(item.code, $event)"
                    />
                    <strong>{{ item.label }}</strong>
                  </label>
                  @if (item.canCreate || item.canUpdate || item.canDelete) {
                    <div class="db-actions-toggles">
                      @if (item.canCreate) {
                        <label class="action-check">
                          <input
                            type="checkbox"
                            [checked]="has(item.code + ':CREATE')"
                            [disabled]="isMandatory(item.code + ':CREATE')"
                            (change)="toggle(item.code + ':CREATE', $event)"
                          />
                          <span>Ajouter</span>
                        </label>
                      }
                      @if (item.canUpdate) {
                        <label class="action-check">
                          <input
                            type="checkbox"
                            [checked]="has(item.code + ':UPDATE')"
                            [disabled]="isMandatory(item.code + ':UPDATE')"
                            (change)="toggle(item.code + ':UPDATE', $event)"
                          />
                          <span>Modifier</span>
                        </label>
                      }
                      @if (item.canDelete) {
                        <label class="action-check">
                          <input
                            type="checkbox"
                            [checked]="has(item.code + ':DELETE')"
                            [disabled]="isMandatory(item.code + ':DELETE')"
                            (change)="toggle(item.code + ':DELETE', $event)"
                          />
                          <span>Supprimer</span>
                        </label>
                      }
                    </div>
                  }
                </div>
              }
            </div>
          }
        </soft-card>
      }
    </div>
  `,
  styles: [
    `
      .user-select-card {
        margin-bottom: 24px;
      }
      .user-selector-bar {
        display: flex;
        align-items: flex-end;
        justify-content: space-between;
        flex-wrap: wrap;
        gap: 16px;
      }
      .user-selector-field {
        flex: 1;
        min-width: 260px;
        max-width: 420px;
      }
      .user-status-badges {
        display: flex;
        align-items: center;
        gap: 8px;
        flex-wrap: wrap;
      }
      .user-role-badge {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        font-size: 13px;
        font-weight: 600;
        padding: 5px 12px;
        border-radius: var(--radius-full);
        background: var(--surface-dark);
        color: var(--app-text);
        border: 1px solid var(--app-border);
      }
      .user-status-tag {
        font-size: 12px;
        font-weight: 600;
        padding: 5px 10px;
        border-radius: var(--radius-full);
        background: var(--surface-dark);
        color: var(--text-secondary);
      }
      .user-status-tag.is-custom {
        background: var(--primary-50);
        color: var(--primary-700);
      }
      .bulk-actions {
        display: flex;
        align-items: center;
        gap: 8px;
        font-size: 13px;
      }
      .action-link-btn {
        background: transparent;
        border: none;
        color: var(--softtime);
        font-weight: 600;
        cursor: pointer;
        padding: 4px 6px;
        font-size: 13px;
      }
      .action-link-btn:hover {
        text-decoration: underline;
      }
      .sep {
        color: var(--text-muted);
      }

      .permissions-container {
        display: flex;
        flex-direction: column;
        gap: 20px;
      }

      .perm-card {
        transition: box-shadow var(--t-fast) var(--ease);
      }

      .perm-card-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        flex-wrap: wrap;
        gap: 12px;
        padding-bottom: 12px;
        border-bottom: 1px solid var(--border-light);
        margin-bottom: 14px;
      }
      .perm-title-group {
        display: flex;
        align-items: center;
        gap: 10px;
      }
      .perm-title-group h3 {
        font-size: 16px;
        font-weight: 700;
      }
      .count-badge {
        font-size: 12px;
        font-weight: 700;
        background: var(--surface-dark);
        color: var(--text-secondary);
        padding: 2px 8px;
        border-radius: var(--radius-full);
      }

      .card-bulk-actions {
        display: flex;
        align-items: center;
        gap: 6px;
      }
      .btn-collapse {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 26px;
        height: 24px;
        padding: 0;
        color: var(--text-secondary);
      }
      .btn-collapse:hover {
        color: var(--softtime);
        background: var(--primary-50);
        border-color: var(--primary-200);
      }
      .perm-card.is-collapsed .perm-card-header {
        border-bottom: none;
        margin-bottom: 0;
        padding-bottom: 0;
      }
      .btn-xs {
        background: var(--surface-dark);
        border: 1px solid var(--color-border);
        border-radius: var(--radius-sm);
        padding: 3px 8px;
        font-size: 12px;
        font-weight: 500;
        color: var(--app-text);
        cursor: pointer;
        transition: all var(--t-fast) var(--ease);
      }
      .btn-xs:hover {
        background: var(--primary-50);
        color: var(--softtime);
        border-color: var(--primary-200);
      }

      .perm-section-desc {
        font-size: 13px;
        color: var(--text-secondary);
        margin: 0 0 16px;
      }
      .empty-hint {
        padding: 16px;
        color: var(--text-muted);
        font-style: italic;
      }

      .db-perm-grid {
        display: flex;
        flex-direction: column;
        gap: 10px;
      }
      .db-perm-row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        flex-wrap: wrap;
        gap: 12px;
        padding: 10px 14px;
        background: var(--surface-dark);
        border-radius: var(--radius-md);
        border: 1px solid var(--border-light);
      }
      .db-perm-name {
        display: flex;
        flex-direction: column;
        min-width: 160px;
      }
      .db-perm-name strong {
        font-size: 14px;
        color: var(--app-text);
      }
      .db-perm-name small {
        font-size: 12px;
        color: var(--text-muted);
      }
      .db-actions-toggles {
        display: flex;
        align-items: center;
        gap: 16px;
        flex-wrap: wrap;
      }
      .action-check {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        font-size: 13px;
        font-weight: 500;
        cursor: pointer;
      }
      .action-check input {
        cursor: pointer;
        width: 15px;
        height: 15px;
      }
      .header-action-check {
        margin-right: 8px;
        font-weight: 600;
        color: var(--softtime);
      }
      .item-access-check {
        min-width: 220px;
      }
    `,
  ],
})
export class PrivilegesPage implements OnInit {
  private readonly usersSvc = inject(UsersService);
  private readonly privSvc = inject(PrivilegesService);
  private readonly sageSvc = inject(SageDatabasesService);
  private readonly pointeuseSvc = inject(PointeuseDatabasesService);
  private readonly session = inject(SessionStore);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);

  readonly users = signal<User[]>([]);
  readonly userOptions = signal<{ value: number; label: string }[]>([]);
  readonly sageDbs = signal<SageDb[]>([]);
  readonly pointeuseDbs = signal<PointeuseDb[]>([]);

  readonly selectedUserId = signal<number | null>(null);
  readonly selectedUser = computed(() => {
    const id = this.selectedUserId();
    return this.users().find((u) => u.id === id) ?? null;
  });

  readonly grantedPermissions = signal<Set<string>>(new Set());
  readonly isCustomized = signal(false);
  readonly saving = signal(false);

  readonly featureCategories: FeatureCategory[] = [
    {
      key: 'PARAMS',
      title: 'Paramètres',
      icon: 'sliders',
      items: [
        { code: 'PARAMS:USERS', label: 'Utilisateurs', canCreate: true, canUpdate: true, canDelete: true },
        { code: 'PARAMS:ROLES', label: 'Rôles', canCreate: true, canUpdate: true, canDelete: true },
        { code: 'PARAMS:PRIVILEGES', label: 'Privilèges' },
        { code: 'PARAMS:DB_ACCESS', label: 'Accès bases' },
        { code: 'PARAMS:CLOCK_PARAMS', label: 'Paramètres pointeuse' },
        { code: 'PARAMS:CORRESPONDENCE', label: 'Correspondance' },
        { code: 'PARAMS:CARDPAIE', label: 'Correspondances paie', canCreate: true, canUpdate: true, canDelete: true },
        { code: 'PARAMS:CATEGORIES', label: 'Catégories', canCreate: true, canUpdate: true, canDelete: true },
        { code: 'PARAMS:AFFECTATIONS', label: 'Affectations', canCreate: true, canUpdate: true, canDelete: true },
        { code: 'PARAMS:TOLERANCES', label: 'Tolérances', canCreate: true, canUpdate: true },
        { code: 'PARAMS:HOLIDAYS', label: 'Jours fériés', canCreate: true, canUpdate: true, canDelete: true },
        { code: 'PARAMS:MAJORATIONS', label: 'Majorations', canUpdate: true },
        { code: 'PARAMS:CODE_CONSTANTES', label: 'Code Constante' },
        { code: 'PARAMS:ABSENCE_CODES', label: 'Codes absence', canCreate: true, canUpdate: true },
      ],
    },
    {
      key: 'PROC',
      title: 'Traitements',
      icon: 'grid',
      items: [
        { code: 'PROC:SHIFTS', label: 'Shifts', canCreate: true, canUpdate: true, canDelete: true },
        { code: 'PROC:EMPLOYEE_PLANNING', label: 'Planning salariés', canCreate: true, canUpdate: true, canDelete: true },
        { code: 'PROC:PUNCHES', label: 'Pointages' },
        { code: 'PROC:ANOMALIES', label: 'Anomalies', canDelete: true },
        { code: 'PROC:CORRECTED_HOURS', label: 'Heures corrigées', canCreate: true, canUpdate: true, canDelete: true },
        { code: 'PROC:WEEKLY_VALIDATION', label: 'Validation semaine' },
        { code: 'PROC:OVERTIME', label: 'Heures supplémentaires' },
      ],
    },
    {
      key: 'OTHER',
      title: 'Autres',
      icon: 'coffee',
      items: [
        { code: 'OTHER:CANTEEN', label: 'Cantine' },
        { code: 'OTHER:TRAVEL_EXPENSES', label: 'Frais déplacement' },
      ],
    },
    {
      key: 'TRACE',
      title: 'Traçabilités',
      icon: 'history',
      items: [{ code: 'TRACE:AUDIT', label: 'Audit et traçabilité' }],
    },
    {
      key: 'REPORTS',
      title: 'États',
      icon: 'bar-chart',
      items: [{ code: 'REPORTS:REPORTS', label: 'Rapports et exports' }],
    },
  ];

  readonly totalSagePerms = computed(() => (this.sageDbs().length > 0 ? 1 + this.sageDbs().length * 3 : 0));
  readonly totalPointeusePerms = computed(() => (this.pointeuseDbs().length > 0 ? 1 + this.pointeuseDbs().length * 3 : 0));

  totalCategoryPerms(cat: FeatureCategory): number {
    return cat.items.reduce((acc, it) => {
      let count = 1;
      if (it.canCreate) count++;
      if (it.canUpdate) count++;
      if (it.canDelete) count++;
      return acc + count;
    }, 0);
  }

  ngOnInit(): void {
    this.sageSvc.list().subscribe((dbs) => this.sageDbs.set(dbs));
    this.pointeuseSvc.list().subscribe((dbs) => this.pointeuseDbs.set(dbs));

    this.usersSvc.list().subscribe((users) => {
      this.users.set(users);
      this.userOptions.set(users.map((u) => ({ value: u.id, label: `${u.nom} (${u.login})` })));
      if (users.length) {
        this.selectedUserId.set(users[0].id);
        this.loadUserPermissions(users[0].id);
      }
    });
  }

  onUserChange(userId: unknown): void {
    const id = Number(userId);
    if (!id) return;
    this.selectedUserId.set(id);
    this.loadUserPermissions(id);
  }

  private loadUserPermissions(userId: number): void {
    this.privSvc.byUser(userId).subscribe({
      next: (res) => {
        this.grantedPermissions.set(new Set(res.grantedPermissions ?? []));
        this.isCustomized.set(res.isCustomized);
      },
      error: () => {
        this.toast.error('Impossible de charger les privilèges de cet utilisateur.');
      },
    });
  }

  readonly isTargetAdmin = computed(() => this.selectedUser()?.roleId === 1);

  isMandatory(code: string): boolean {
    if (!this.isTargetAdmin()) return false;
    return (
      code.startsWith('SAGE:') ||
      code.startsWith('POINTEUSE:') ||
      code === 'PARAMS:PRIVILEGES' ||
      code === 'PARAMS:ROLES' ||
      code === 'PARAMS:USERS' ||
      code === 'PARAMS:DB_ACCESS'
    );
  }

  private addMandatoryPermissions(set: Set<string>): void {
    set.add('SAGE:CREATE');
    for (const db of this.sageDbs()) {
      set.add(`SAGE:${db.nomBd}:READ`);
      set.add(`SAGE:${db.nomBd}:UPDATE`);
      set.add(`SAGE:${db.nomBd}:DELETE`);
    }
    set.add('POINTEUSE:CREATE');
    for (const db of this.pointeuseDbs()) {
      set.add(`POINTEUSE:${db.nomBd}:READ`);
      set.add(`POINTEUSE:${db.nomBd}:UPDATE`);
      set.add(`POINTEUSE:${db.nomBd}:DELETE`);
    }
    set.add('PARAMS:PRIVILEGES');
    set.add('PARAMS:ROLES');
    set.add('PARAMS:USERS');
    set.add('PARAMS:DB_ACCESS');
  }

  has(code: string): boolean {
    if (this.isMandatory(code)) return true;
    return this.grantedPermissions().has(code);
  }

  toggle(code: string, event: Event): void {
    if (this.isMandatory(code)) {
      (event.target as HTMLInputElement).checked = true;
      this.toast.info('Cette permission est obligatoire pour un compte administrateur.');
      return;
    }
    const checked = (event.target as HTMLInputElement).checked;
    this.grantedPermissions.update((set) => {
      const next = new Set(set);
      if (checked) next.add(code);
      else next.delete(code);
      return next;
    });
  }

  readonly collapsedSections = signal<Set<string>>(
    new Set(['SAGE', 'POINTEUSE', 'PARAMS', 'PROC', 'OTHER', 'TRACE', 'REPORTS']),
  );

  isCollapsed(key: string): boolean {
    return this.collapsedSections().has(key);
  }

  toggleCollapse(key: string): void {
    this.collapsedSections.update((set) => {
      const next = new Set(set);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  countActiveCategory(prefix: string): number {
    if (prefix === 'SAGE') {
      let count = this.has('SAGE:CREATE') ? 1 : 0;
      for (const db of this.sageDbs()) {
        if (this.has(`SAGE:${db.nomBd}:READ`)) count++;
        if (this.has(`SAGE:${db.nomBd}:UPDATE`)) count++;
        if (this.has(`SAGE:${db.nomBd}:DELETE`)) count++;
      }
      return count;
    }
    if (prefix === 'POINTEUSE') {
      let count = this.has('POINTEUSE:CREATE') ? 1 : 0;
      for (const db of this.pointeuseDbs()) {
        if (this.has(`POINTEUSE:${db.nomBd}:READ`)) count++;
        if (this.has(`POINTEUSE:${db.nomBd}:UPDATE`)) count++;
        if (this.has(`POINTEUSE:${db.nomBd}:DELETE`)) count++;
      }
      return count;
    }
    const cat = this.featureCategories.find((c) => c.key === prefix);
    if (cat) {
      let count = 0;
      for (const item of cat.items) {
        if (this.has(item.code)) count++;
        if (item.canCreate && this.has(`${item.code}:CREATE`)) count++;
        if (item.canUpdate && this.has(`${item.code}:UPDATE`)) count++;
        if (item.canDelete && this.has(`${item.code}:DELETE`)) count++;
      }
      return count;
    }
    return 0;
  }

  toggleGroup(prefix: string, enable: boolean): void {
    if (!enable && this.isTargetAdmin() && (prefix === 'SAGE' || prefix === 'POINTEUSE')) {
      this.toast.info("L'accès aux bases ne peut pas être retiré à un administrateur.");
      return;
    }
    this.grantedPermissions.update((set) => {
      const next = new Set(set);
      if (prefix === 'SAGE') {
        if (enable) next.add('SAGE:CREATE');
        else next.delete('SAGE:CREATE');
        for (const db of this.sageDbs()) {
          const codes = [
            `SAGE:${db.nomBd}:READ`,
            `SAGE:${db.nomBd}:UPDATE`,
            `SAGE:${db.nomBd}:DELETE`,
          ];
          for (const c of codes) {
            if (enable) next.add(c);
            else next.delete(c);
          }
        }
      } else if (prefix === 'POINTEUSE') {
        if (enable) next.add('POINTEUSE:CREATE');
        else next.delete('POINTEUSE:CREATE');
        for (const db of this.pointeuseDbs()) {
          const codes = [
            `POINTEUSE:${db.nomBd}:READ`,
            `POINTEUSE:${db.nomBd}:UPDATE`,
            `POINTEUSE:${db.nomBd}:DELETE`,
          ];
          for (const c of codes) {
            if (enable) next.add(c);
            else next.delete(c);
          }
        }
      } else {
        const cat = this.featureCategories.find((c) => c.key === prefix);
        if (cat) {
          for (const item of cat.items) {
            const itemCodes = [item.code];
            if (item.canCreate) itemCodes.push(item.code + ':CREATE');
            if (item.canUpdate) itemCodes.push(item.code + ':UPDATE');
            if (item.canDelete) itemCodes.push(item.code + ':DELETE');
            for (const c of itemCodes) {
              if (enable) next.add(c);
              else if (!this.isMandatory(c)) next.delete(c);
            }
          }
        }
      }
      return next;
    });
  }

  toggleAll(enable: boolean): void {
    if (!enable) {
      if (this.isTargetAdmin()) {
        const next = new Set<string>();
        this.addMandatoryPermissions(next);
        this.grantedPermissions.set(next);
        this.toast.info("Les bases et privilèges d'administration restent obligatoirement activés pour ce profil.");
        return;
      }
      this.grantedPermissions.set(new Set());
      return;
    }
    const next = new Set<string>();
    // SAGE
    if (this.sageDbs().length > 0) {
      next.add('SAGE:CREATE');
      for (const db of this.sageDbs()) {
        next.add(`SAGE:${db.nomBd}:READ`);
        next.add(`SAGE:${db.nomBd}:UPDATE`);
        next.add(`SAGE:${db.nomBd}:DELETE`);
      }
    }
    // POINTEUSE
    if (this.pointeuseDbs().length > 0) {
      next.add('POINTEUSE:CREATE');
      for (const db of this.pointeuseDbs()) {
        next.add(`POINTEUSE:${db.nomBd}:READ`);
        next.add(`POINTEUSE:${db.nomBd}:UPDATE`);
        next.add(`POINTEUSE:${db.nomBd}:DELETE`);
      }
    }
    // Categories
    for (const cat of this.featureCategories) {
      for (const item of cat.items) {
        next.add(item.code);
        if (item.canCreate) next.add(item.code + ':CREATE');
        if (item.canUpdate) next.add(item.code + ':UPDATE');
        if (item.canDelete) next.add(item.code + ':DELETE');
      }
    }
    this.grantedPermissions.set(next);
  }

  save(): void {
    const userId = this.selectedUserId();
    if (!userId) return;

    // Protection anti-escalade côté client
    const currentSessionUser = this.session.session();
    if (currentSessionUser && currentSessionUser.userId === userId && currentSessionUser.roleId !== 1) {
      this.toast.error("Vous ne pouvez pas modifier vos propres privilèges sans droit d'administration.");
      return;
    }

    if (this.isTargetAdmin()) {
      const current = new Set(this.grantedPermissions());
      this.addMandatoryPermissions(current);
      this.grantedPermissions.set(current);
    }

    this.saving.set(true);
    const perms = Array.from(this.grantedPermissions());
    this.privSvc.saveUserPermissions(userId, perms).subscribe({
      next: () => {
        this.saving.set(false);
        this.isCustomized.set(true);
        if (currentSessionUser && currentSessionUser.userId === userId) {
          this.session.setPermissions(perms);
          this.auth.refreshMe().subscribe();
        }
        this.toast.success('Permissions enregistrées avec succès.');
      },
      error: (e) => {
        this.saving.set(false);
        const err = e?.error?.message || e?.error?.title || e?.error || "Erreur lors de l'enregistrement.";
        this.toast.error(err);
      },
    });
  }

  getRoleLabel(roleId: number): string {
    switch (roleId) {
      case 1:
        return 'Administrateur';
      case 2:
        return 'Responsable RH';
      case 3:
        return 'Opérateur';
      default:
        return `Rôle #${roleId}`;
    }
  }
}
