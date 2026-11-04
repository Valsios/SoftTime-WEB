import { ChangeDetectionStrategy, Component, computed, effect, HostListener, inject, OnInit, signal, untracked } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { ToastService } from '../../core/toast.service';
import { SessionStore } from '../../core/session.store';
import { PointeuseDb, Role, SageDb, User } from '../../shared/models';
import { PointeuseDatabasesService } from '../../shared/services/catalog.service';
import { PrivilegesService, RolesService, SageDatabasesService, UsersService } from '../../shared/services/users.service';
import { PrivilegesDraftService } from '../../shared/services/privileges-draft.service';
import { AuthService } from '../../shared/services/auth.service';
import { Icon } from '../../shared/components/icon';
import { PermMark } from './perm-mark';
import { PermSearch } from './perm-search';
import {
  SENSITIVE_RIGHT_HINT,
  applyToggle,
  computeWarnings,
  filterCategories,
  isSensitiveRight,
  matchesAnySearch,
} from './privileges.advice';
import {
  appliesAtNextLoginNote,
  buildResetToRoleMessage,
  buildRoleImpactMessage,
  canEditRolePermissions,
  computeSources,
  customizedBadgeLabel,
  countUsersOfRole,
  diffPermissions,
  extractErrorMessage,
  isAdminRole,
  resolveRoleLabel,
} from './privileges.logic';

type PrivilegesMode = 'user' | 'role';

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
  imports: [CommonModule, FormsModule, Icon, PermMark, PermSearch],
  template: `
    <!-- En-tête de page - Conforme Charte Softwell §7.1 N°3 et N°4 -->
    <header class="sw-page-header">
      <div class="sw-page-header-main">
        <div class="sw-eyebrow">
          <span class="sw-eyebrow-line"></span>
          <span class="sw-eyebrow-text">CONFIGURATION · SÉCURITÉ</span>
        </div>
        <h1 class="sw-page-title">Privilèges d'accès</h1>
        <p class="sw-page-subtitle">Permissions détaillées par utilisateur, base de données et fonctionnalité</p>
      </div>

      <div class="sw-page-header-actions">
        <button
          type="button"
          class="sw-btn-primary"
          [disabled]="saving() || !hasTarget() || permissionsLocked()"
          (click)="save()"
        >
          @if (saving()) {
            <span class="sw-spinner"></span>
            <span>Enregistrement...</span>
          } @else {
            <app-icon name="check" [size]="16"></app-icon>
            <span>Enregistrer les modifications</span>
          }
        </button>
      </div>
    </header>

    @if (isDirty()) {
      <section class="sw-card sw-draft-bar" role="status" aria-label="Modifications non enregistrées">
        <div class="sw-draft-bar-content">
          <strong class="sw-draft-bar-title">Modifications non enregistrées</strong>
          <span class="sw-draft-bar-text">{{ changesCount() }} modification(s) non enregistrée(s)</span>
          @if (serverChangedWarning()) {
            <span class="sw-draft-bar-warning">Les droits ont changé sur le serveur depuis votre dernière modification.</span>
          }
          @for (w of warnings(); track w) {
            <span class="sw-draft-bar-warning">{{ w }}</span>
          }
          @if (otherDirtyCount() > 0) {
            <span class="sw-draft-bar-others">Brouillons en attente pour {{ otherDirtyCount() }} autre(s) cible(s).</span>
          }
        </div>
        <div class="sw-draft-bar-actions">
          <button
            type="button"
            class="sw-btn-sm"
            [disabled]="saving()"
            (click)="discardChanges()"
          >
            Annuler
          </button>
          <button
            type="button"
            class="sw-btn-sm sw-btn-primary"
            [disabled]="saving() || permissionsLocked()"
            (click)="save()"
          >
            Enregistrer
          </button>
        </div>
      </section>
    }

    <!-- Sélecteur de mode : par utilisateur ou par rôle -->
    <section class="sw-card sw-mode-card" aria-label="Mode de configuration">
      <div class="sw-segmented" role="group" aria-label="Mode de configuration">
        <button
          type="button"
          class="sw-segment-btn"
          [class.is-active]="mode() === 'user'"
          [attr.aria-pressed]="mode() === 'user'"
          (click)="setMode('user')"
        >
          Par utilisateur
        </button>
        <button
          type="button"
          class="sw-segment-btn"
          [class.is-active]="mode() === 'role'"
          [attr.aria-pressed]="mode() === 'role'"
          (click)="setMode('role')"
        >
          Par rôle
        </button>
      </div>
    </section>

    @if (roleEditLocked()) {
      <section class="sw-card" aria-label="Modification réservée">
        <p class="sw-empty-hint">Seul un administrateur peut modifier les droits d'un rôle.</p>
      </section>
    }

    <!-- Barre de sélection compacte -->
    <section class="sw-card sw-user-select-card" [attr.aria-label]="mode() === 'user' ? 'Utilisateur à configurer' : 'Rôle à configurer'">
      <div class="sw-user-selector-bar">
        @if (mode() === 'user') {
          <div class="sw-user-selector-field">
            <label class="sw-field-label" for="swUserSelect">
              Utilisateur à configurer <span class="sw-req">*</span>
            </label>
            <div class="sw-select-wrap">
              <select
                id="swUserSelect"
                class="sw-select"
                [ngModel]="selectedUserId()"
                (ngModelChange)="onUserChange($event)"
              >
                @for (opt of userOptions(); track opt.value) {
                  <option [value]="opt.value">{{ opt.label }}</option>
                }
              </select>
            </div>
          </div>

          <div class="sw-user-status-badges" *ngIf="selectedUser() as u">
            <span class="sw-badge sw-badge-role">
              <app-icon name="shield" [size]="14"></app-icon>
              Rôle : {{ getRoleLabel(u.roleId) }}
            </span>
            <span class="sw-badge" [class.sw-badge-custom]="adminLocked() || showCustomized()" [class.sw-badge-default]="!adminLocked() && !showCustomized()">
              {{ badgeLabel() }}
            </span>
          </div>

          @if (!adminLocked()) {
            <div class="sw-bulk-actions">
              <button type="button" class="sw-btn-sm" [disabled]="permissionsLocked()" (click)="toggleAll(true)">
                Tout cocher
              </button>
              <button
                type="button"
                class="sw-btn-sm"
                [disabled]="permissionsLocked()"
                (click)="toggleAll(false)"
                title="Tout décocher"
              >
                Tout décocher
              </button>
              @if (canResetToRole()) {
                <button
                  type="button"
                  class="sw-btn-sm"
                  [disabled]="saving() || permissionsLoading()"
                  (click)="resetToRole()"
                  title="Effacer les ajustements personnels et reprendre les droits du rôle"
                >
                  Revenir aux droits du rôle
                </button>
              }
            </div>
          }
        } @else {
          <div class="sw-user-selector-field">
            <label class="sw-field-label" for="swRoleSelect">
              Rôle à configurer <span class="sw-req">*</span>
            </label>
            <div class="sw-select-wrap">
              <select
                id="swRoleSelect"
                class="sw-select"
                [ngModel]="selectedRoleId()"
                (ngModelChange)="onRoleChange($event)"
              >
                @for (opt of roleOptions(); track opt.value) {
                  <option [value]="opt.value">{{ opt.label }}</option>
                }
              </select>
            </div>
          </div>

          <div class="sw-user-status-badges">
            <span class="sw-badge sw-badge-role">
              <app-icon name="shield" [size]="14"></app-icon>
              Rôle : {{ selectedRoleLabel() }}
            </span>
            @if (adminLocked()) {
              <span class="sw-badge sw-badge-custom">Tous les droits</span>
            }
          </div>
        }
      </div>

        @if (mode() === 'role' && !isRoleConfigured() && !adminLocked()) {
        <p class="sw-empty-hint">
          Ce rôle n'a pas encore de configuration détaillée : les droits affichés sont déduits de ses grandes portes
          actuelles.
        </p>
      }
    </section>

    @if (adminLocked()) {
      <div class="sw-admin-banner" role="status">
        <app-icon name="shield" [size]="16"></app-icon>
        <span>Droits complets garantis par le serveur. Non modifiables.</span>
      </div>
    }

    <!-- Conteneur des permissions par catégorie -->
    <div class="sw-permissions-container" *ngIf="hasTarget()">
      <!-- Section 1 : Bases RH / Paie (Charte §7.1 N°8 & §8.4) -->
      <perm-search [value]="search()" (changed)="search.set($event)"></perm-search>

      @if (matchesAnySearch(sageSearchLabels(), search())) {
      <section class="sw-card sw-perm-card" [class.is-collapsed]="isCollapsed('SAGE')">
        <div class="sw-perm-card-header">
          <div class="sw-perm-title-group">
            <div class="sw-icon-pill">
              <app-icon name="server" [size]="20"></app-icon>
            </div>
            <div>
              <h3 class="sw-category-title">Bases RH / Paie</h3>
              <p class="sw-category-desc">Permissions d'accès et d'actions (Consulter, Modifier, Supprimer) par base SAGE</p>
            </div>
            <span class="sw-counter-badge">{{ countActiveCategory('SAGE') }} / {{ totalSagePerms() }}</span>
          </div>

          <div class="sw-card-bulk-actions">
            <label class="sw-check-label sw-header-check-label" [class.is-disabled]="isReadOnly('SAGE:CREATE')">
              <input
                type="checkbox"
                [checked]="has('SAGE:CREATE')"
                [disabled]="isReadOnly('SAGE:CREATE')"
                (change)="toggle('SAGE:CREATE', $event)"
              />
              <perm-mark [kind]="mark('SAGE:CREATE')"></perm-mark>
              <span>Ajouter une base</span>
              @if (isMandatory('SAGE:CREATE')) {
                <span class="sw-lock-indicator" title="Obligatoire pour le profil Administrateur">
                  <app-icon name="shield" [size]="12"></app-icon>
                </span>
              }
            </label>
            <button type="button" class="sw-btn-xs" [disabled]="permissionsLocked()" (click)="toggleGroup('SAGE', true)">Tout cocher</button>
            <button type="button" class="sw-btn-xs" (click)="toggleGroup('SAGE', false)" [disabled]="permissionsLocked()">Tout décocher</button>
            <button
              type="button"
              class="sw-btn-icon-collapse"
              [title]="isCollapsed('SAGE') ? 'Déplier la section' : 'Replier la section'"
              (click)="toggleCollapse('SAGE')"
            >
              <app-icon [name]="isCollapsed('SAGE') ? 'chevron-down' : 'chevron-up'" [size]="16"></app-icon>
            </button>
          </div>
        </div>

        @if (!isCollapsed('SAGE')) {
          <div class="sw-card-content">
            @if (!sageDbs().length) {
              <div class="sw-empty-hint">Aucune base SAGE configurée dans le système.</div>
            } @else {
              <div class="sw-perm-rows-list">
                @for (db of sageDbs(); track db.id) {
                  <div class="sw-perm-row">
                    <div class="sw-perm-name-col">
                      <strong class="sw-perm-item-title">{{ db.nomBd }}</strong>
                      <span class="sw-perm-item-subtitle">{{ db.serveur || 'Serveur configuré' }}</span>
                    </div>

                    <div class="sw-actions-toggles">
                      <label class="sw-check-label" [class.is-disabled]="isReadOnly('SAGE:' + db.nomBd + ':READ')">
                        <input
                          type="checkbox"
                          [checked]="has('SAGE:' + db.nomBd + ':READ')"
                          [disabled]="isReadOnly('SAGE:' + db.nomBd + ':READ')"
                          (change)="toggle('SAGE:' + db.nomBd + ':READ', $event)"
                        />
                        <perm-mark [kind]="mark('SAGE:' + db.nomBd + ':READ')"></perm-mark>
                        <span>Consulter</span>
                        @if (isMandatory('SAGE:' + db.nomBd + ':READ')) {
                          <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                        }
                      </label>

                      <label class="sw-check-label" [class.is-disabled]="isReadOnly('SAGE:' + db.nomBd + ':UPDATE')">
                        <input
                          type="checkbox"
                          [checked]="has('SAGE:' + db.nomBd + ':UPDATE')"
                          [disabled]="isReadOnly('SAGE:' + db.nomBd + ':UPDATE')"
                          (change)="toggle('SAGE:' + db.nomBd + ':UPDATE', $event)"
                        />
                        <perm-mark [kind]="mark('SAGE:' + db.nomBd + ':UPDATE')"></perm-mark>
                        <span>Modifier</span>
                        @if (isMandatory('SAGE:' + db.nomBd + ':UPDATE')) {
                          <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                        }
                      </label>

                      <label class="sw-check-label" [class.is-disabled]="isReadOnly('SAGE:' + db.nomBd + ':DELETE')">
                        <input
                          type="checkbox"
                          [checked]="has('SAGE:' + db.nomBd + ':DELETE')"
                          [disabled]="isReadOnly('SAGE:' + db.nomBd + ':DELETE')"
                          (change)="toggle('SAGE:' + db.nomBd + ':DELETE', $event)"
                        />
                        <perm-mark [kind]="mark('SAGE:' + db.nomBd + ':DELETE')"></perm-mark>
                        <span>Supprimer</span>
                        @if (isMandatory('SAGE:' + db.nomBd + ':DELETE')) {
                          <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                        }
                      </label>
                    </div>
                  </div>
                }
              </div>
            }
          </div>
        }
      </section>
      }

      <!-- Section 2 : Bases de pointage (Charte §7.1 N°8 & §8.4) -->
      @if (matchesAnySearch(pointeuseSearchLabels(), search())) {
      <section class="sw-card sw-perm-card" [class.is-collapsed]="isCollapsed('POINTEUSE')">
        <div class="sw-perm-card-header">
          <div class="sw-perm-title-group">
            <div class="sw-icon-pill">
              <app-icon name="clock" [size]="20"></app-icon>
            </div>
            <div>
              <h3 class="sw-category-title">Bases de pointage</h3>
              <p class="sw-category-desc">Permissions d'accès et d'actions (Consulter, Modifier, Supprimer) par pointeuse</p>
            </div>
            <span class="sw-counter-badge">{{ countActiveCategory('POINTEUSE') }} / {{ totalPointeusePerms() }}</span>
          </div>

          <div class="sw-card-bulk-actions">
            <label class="sw-check-label sw-header-check-label" [class.is-disabled]="isReadOnly('POINTEUSE:CREATE')">
              <input
                type="checkbox"
                [checked]="has('POINTEUSE:CREATE')"
                [disabled]="isReadOnly('POINTEUSE:CREATE')"
                (change)="toggle('POINTEUSE:CREATE', $event)"
              />
              <perm-mark [kind]="mark('POINTEUSE:CREATE')"></perm-mark>
              <span>Ajouter une base</span>
              @if (isMandatory('POINTEUSE:CREATE')) {
                <span class="sw-lock-indicator" title="Obligatoire pour le profil Administrateur">
                  <app-icon name="shield" [size]="12"></app-icon>
                </span>
              }
            </label>
            <button type="button" class="sw-btn-xs" [disabled]="permissionsLocked()" (click)="toggleGroup('POINTEUSE', true)">Tout cocher</button>
            <button type="button" class="sw-btn-xs" (click)="toggleGroup('POINTEUSE', false)" [disabled]="permissionsLocked()">Tout décocher</button>
            <button
              type="button"
              class="sw-btn-icon-collapse"
              [title]="isCollapsed('POINTEUSE') ? 'Déplier la section' : 'Replier la section'"
              (click)="toggleCollapse('POINTEUSE')"
            >
              <app-icon [name]="isCollapsed('POINTEUSE') ? 'chevron-down' : 'chevron-up'" [size]="16"></app-icon>
            </button>
          </div>
        </div>

        @if (!isCollapsed('POINTEUSE')) {
          <div class="sw-card-content">
            @if (!pointeuseDbs().length) {
              <div class="sw-empty-hint">Aucune base pointeuse configurée dans le système.</div>
            } @else {
              <div class="sw-perm-rows-list">
                @for (db of pointeuseDbs(); track db.id) {
                  <div class="sw-perm-row">
                    <div class="sw-perm-name-col">
                      <strong class="sw-perm-item-title">{{ db.nomBd }}</strong>
                      <span class="sw-perm-item-subtitle">{{ db.serveur || 'Pointeuse réseau' }}</span>
                    </div>

                    <div class="sw-actions-toggles">
                      <label class="sw-check-label" [class.is-disabled]="isReadOnly('POINTEUSE:' + db.nomBd + ':READ')">
                        <input
                          type="checkbox"
                          [checked]="has('POINTEUSE:' + db.nomBd + ':READ')"
                          [disabled]="isReadOnly('POINTEUSE:' + db.nomBd + ':READ')"
                          (change)="toggle('POINTEUSE:' + db.nomBd + ':READ', $event)"
                        />
                        <perm-mark [kind]="mark('POINTEUSE:' + db.nomBd + ':READ')"></perm-mark>
                        <span>Consulter</span>
                        @if (isMandatory('POINTEUSE:' + db.nomBd + ':READ')) {
                          <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                        }
                      </label>

                      <label class="sw-check-label" [class.is-disabled]="isReadOnly('POINTEUSE:' + db.nomBd + ':UPDATE')">
                        <input
                          type="checkbox"
                          [checked]="has('POINTEUSE:' + db.nomBd + ':UPDATE')"
                          [disabled]="isReadOnly('POINTEUSE:' + db.nomBd + ':UPDATE')"
                          (change)="toggle('POINTEUSE:' + db.nomBd + ':UPDATE', $event)"
                        />
                        <perm-mark [kind]="mark('POINTEUSE:' + db.nomBd + ':UPDATE')"></perm-mark>
                        <span>Modifier</span>
                        @if (isMandatory('POINTEUSE:' + db.nomBd + ':UPDATE')) {
                          <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                        }
                      </label>

                      <label class="sw-check-label" [class.is-disabled]="isReadOnly('POINTEUSE:' + db.nomBd + ':DELETE')">
                        <input
                          type="checkbox"
                          [checked]="has('POINTEUSE:' + db.nomBd + ':DELETE')"
                          [disabled]="isReadOnly('POINTEUSE:' + db.nomBd + ':DELETE')"
                          (change)="toggle('POINTEUSE:' + db.nomBd + ':DELETE', $event)"
                        />
                        <perm-mark [kind]="mark('POINTEUSE:' + db.nomBd + ':DELETE')"></perm-mark>
                        <span>Supprimer</span>
                        @if (isMandatory('POINTEUSE:' + db.nomBd + ':DELETE')) {
                          <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                        }
                      </label>
                    </div>
                  </div>
                }
              </div>
            }
          </div>
        }
      </section>
      }

      <!-- Sections 3 à 7 : Catégories fonctionnelles (Charte §7.1 & §8.4) -->
      @for (cat of visibleCategories(); track cat.key) {
        <section class="sw-card sw-perm-card" [class.is-collapsed]="isCollapsed(cat.key)">
          <div class="sw-perm-card-header">
            <div class="sw-perm-title-group">
              <div class="sw-icon-pill">
                <app-icon [name]="cat.icon" [size]="20"></app-icon>
              </div>
              <div>
                <h3 class="sw-category-title">{{ cat.title }}</h3>
                <p class="sw-category-desc">Droits d'accès et opérations autorisées pour {{ cat.title }}</p>
              </div>
              <span class="sw-counter-badge">{{ countActiveCategory(cat.key) }} / {{ totalCategoryPerms(cat) }}</span>
            </div>

            <div class="sw-card-bulk-actions">
              <button type="button" class="sw-btn-xs" [disabled]="permissionsLocked()" (click)="toggleGroup(cat.key, true)">Tout cocher</button>
              <button type="button" class="sw-btn-xs" [disabled]="permissionsLocked()" (click)="toggleGroup(cat.key, false)">Tout décocher</button>
              <button
                type="button"
                class="sw-btn-icon-collapse"
                [title]="isCollapsed(cat.key) ? 'Déplier la section' : 'Replier la section'"
                (click)="toggleCollapse(cat.key)"
              >
                <app-icon [name]="isCollapsed(cat.key) ? 'chevron-down' : 'chevron-up'" [size]="16"></app-icon>
              </button>
            </div>
          </div>

          @if (!isCollapsed(cat.key)) {
            <div class="sw-card-content">
              <div class="sw-perm-rows-list">
                @for (item of cat.items; track item.code) {
                  <div class="sw-perm-row">
                    <label class="sw-check-label sw-item-access-check" [class.is-disabled]="isReadOnly(item.code)">
                      <input
                        type="checkbox"
                        [checked]="has(item.code)"
                        [disabled]="isReadOnly(item.code)"
                        (change)="toggle(item.code, $event)"
                      />
                      <perm-mark [kind]="mark(item.code)"></perm-mark>
                      <strong class="sw-perm-item-title">{{ item.label }}</strong>
                      @if (isMandatory(item.code)) {
                        <span class="sw-lock-indicator" title="Obligatoire pour le profil Administrateur">
                          <app-icon name="shield" [size]="12"></app-icon>
                        </span>
                      }
                      @if (isSensitive(item.code)) {
                        <span class="sw-sensitive" role="img" [attr.title]="sensitiveHint" [attr.aria-label]="sensitiveHint">
                          <app-icon name="shield" [size]="12"></app-icon>
                        </span>
                      }
                    </label>

                    @if (item.canCreate || item.canUpdate || item.canDelete) {
                      <div class="sw-actions-toggles">
                        @if (item.canCreate) {
                          <label class="sw-check-label" [class.is-disabled]="isReadOnly(item.code + ':CREATE')">
                            <input
                              type="checkbox"
                              [checked]="has(item.code + ':CREATE')"
                              [disabled]="isReadOnly(item.code + ':CREATE')"
                              (change)="toggle(item.code + ':CREATE', $event)"
                            />
                            <perm-mark [kind]="mark(item.code + ':CREATE')"></perm-mark>
                            <span>Ajouter</span>
                            @if (isMandatory(item.code + ':CREATE')) {
                              <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                            }
                          </label>
                        }
                        @if (item.canUpdate) {
                          <label class="sw-check-label" [class.is-disabled]="isReadOnly(item.code + ':UPDATE')">
                            <input
                              type="checkbox"
                              [checked]="has(item.code + ':UPDATE')"
                              [disabled]="isReadOnly(item.code + ':UPDATE')"
                              (change)="toggle(item.code + ':UPDATE', $event)"
                            />
                            <perm-mark [kind]="mark(item.code + ':UPDATE')"></perm-mark>
                            <span>Modifier</span>
                            @if (isMandatory(item.code + ':UPDATE')) {
                              <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                            }
                          </label>
                        }
                        @if (item.canDelete) {
                          <label class="sw-check-label" [class.is-disabled]="isReadOnly(item.code + ':DELETE')">
                            <input
                              type="checkbox"
                              [checked]="has(item.code + ':DELETE')"
                              [disabled]="isReadOnly(item.code + ':DELETE')"
                              (change)="toggle(item.code + ':DELETE', $event)"
                            />
                            <perm-mark [kind]="mark(item.code + ':DELETE')"></perm-mark>
                            <span>Supprimer</span>
                            @if (isMandatory(item.code + ':DELETE')) {
                              <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                            }
                          </label>
                        }
                      </div>
                    }
                  </div>
                }
              </div>
            </div>
          }
        </section>
      }
    </div>
  `,
  styles: [
    `
      /* ==========================================================================
         Charte Graphique Softwell v1.0 — Styles dédiés Écran Privilèges
         Palette : Navy-700 , Navy-800, Navy-950
                   Gold-400, Gold-300, Line-input
         Typographie : Outfit (titres) & Inter (corps, boutons, libellés)
         ========================================================================== */

      :host {
        display: block;
        color: var(--app-text); /* neutral-800 */
        font-family: 'Inter', system-ui, -apple-system, sans-serif;
      }

      /* ---- 1. En-tête de page (§7.1 N°3 et N°4) ---- */
      .sw-page-header {
        display: flex;
        align-items: flex-start;
        justify-content: space-between;
        flex-wrap: wrap;
        gap: 16px;
        margin-bottom: 24px;
        padding-bottom: 20px;
        border-bottom: 1px solid var(--app-border); /* neutral-200 */
      }

      .sw-page-header-main {
        display: flex;
        flex-direction: column;
        gap: 4px;
      }

      .sw-eyebrow {
        display: inline-flex;
        align-items: center;
        gap: 8px;
        margin-bottom: 2px;
      }

      .sw-eyebrow-line {
        display: inline-block;
        width: 16px;
        height: 2px;
        background-color: var(--color-gold-400); /* gold-400 - filet or de la charte */
        border-radius: 1px;
      }

      .sw-eyebrow-text {
        font-size: 12px;
        font-weight: 600;
        letter-spacing: 0.08em;
        text-transform: uppercase;
        color: var(--text-secondary); /* neutral-600 */
      }

      .sw-page-title {
        margin: 0;
        font-family: 'Outfit', 'Inter', system-ui, sans-serif;
        font-size: 24px;
        font-weight: 600;
        line-height: 1.25;
        letter-spacing: -0.02em;
        color: var(--app-text-strong); /* navy-950 */
      }

      .sw-page-subtitle {
        margin: 0;
        font-size: 14px;
        line-height: 1.5;
        color: var(--text-secondary); /* neutral-600 */
      }

      .sw-page-header-actions {
        display: flex;
        align-items: center;
        gap: 12px;
      }

      /* ---- Bouton Principal Console (§8.1 & §4.1) ---- */
      .sw-btn-primary {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        gap: 8px;
        min-height: 44px; /* Hauteur standard 44px */
        padding: 10px 20px;
        background-color: var(--accent); /* navy-700 Marine Softwell */
        color: var(--text-on-accent);
        font-size: 14px;
        font-weight: 700;
        font-family: 'Inter', system-ui, sans-serif;
        border: none;
        border-radius: 8px; /* rounded-xl charte = 8px */
        box-shadow: var(--shadow-xs);
        cursor: pointer;
        transition: background-color 150ms cubic-bezier(0.2, 0, 0, 1), transform 150ms ease;
        white-space: nowrap;
      }

      .sw-btn-primary:hover:not(:disabled) {
        background-color: var(--accent-hover); /* navy-800 survol */
      }

      .sw-btn-primary:focus-visible {
        outline: 3px solid var(--softtime);
        outline-offset: 2px;
      }

      .sw-btn-primary:disabled {
        opacity: 0.5;
        cursor: not-allowed;
      }

      .sw-spinner {
        display: inline-block;
        width: 14px;
        height: 14px;
        border: 2px solid var(--spinner-track);
        border-top-color: var(--spinner-head);
        border-radius: 50%;
        animation: sw-spin 0.6s linear infinite;
      }

      @keyframes sw-spin {
        to { transform: rotate(360deg); }
      }

      /* ---- 2. Carte (§8.4) & Sélecteur d'utilisateur ---- */
      .sw-card {
        background-color: var(--app-surface);
        border: 1px solid var(--app-border); /* neutral-200 */
        border-radius: 12px; /* rounded-2xl charte = 12px */
        box-shadow: var(--shadow-xs);
      }

      .sw-mode-card {
        padding: 12px 16px;
        margin-bottom: 16px;
      }

      .sw-segmented {
        display: flex;
        border: 1px solid var(--app-border);
        background: var(--app-surface-soft);
        border-radius: var(--radius-md);
        padding: 3px;
        gap: 3px;
        max-width: 380px;
      }

      .sw-segment-btn {
        flex: 1;
        min-height: 36px;
        padding: 6px 16px;
        border: none;
        border-radius: var(--radius-sm);
        background: none;
        color: var(--app-text);
        font-family: var(--font-sans);
        font-size: 13px;
        font-weight: 600;
        cursor: pointer;
        transition: all var(--t-fast) var(--ease);
        text-align: center;
      }

      .sw-segment-btn:hover:not(.is-active) {
        color: var(--app-text-strong);
      }

      .sw-segment-btn.is-active {
        background: var(--softtime);
        color: var(--text-on-accent);
        box-shadow: var(--shadow-xs);
      }

      .sw-user-select-card {
        padding: 20px 24px;
        margin-bottom: 24px;
      }

      .sw-user-selector-bar {
        display: flex;
        align-items: flex-end;
        flex-wrap: wrap;
        gap: 16px;
      }

      .sw-user-selector-bar .sw-bulk-actions {
        margin-left: auto;
      }

      .sw-admin-banner {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 8px 16px;
        margin-top: -12px;
        margin-bottom: 24px;
        background: var(--app-surface-soft);
        border-left: 3px solid var(--color-gold-400);
        border-radius: var(--radius-sm);
        color: var(--app-text);
        font-size: 13px;
        font-weight: 500;
      }

      .sw-user-selector-field {
        flex: 1;
        min-width: 260px;
        max-width: 380px;
      }

      .sw-field-label {
        display: block;
        font-size: 14px;
        font-weight: 600;
        color: var(--app-text); /* neutral-800 casse de phrase */
        margin-bottom: 6px;
      }

      .sw-req {
        color: var(--danger); /* rose-700 / danger */
        font-weight: 700;
      }

      .sw-select-wrap {
        position: relative;
      }

      .sw-select {
        width: 100%;
        height: 44px; /* Hauteur 44px §6.2 */
        padding: 0 14px;
        font-size: 14px;
        font-weight: 500;
        font-family: 'Inter', system-ui, sans-serif;
        color: var(--app-text-strong); /* neutral-900 */
        background-color: var(--app-surface);
        border: 1px solid var(--color-border-input); /* line-input 3,3:1 conforme AA */
        border-radius: 8px; /* rounded-xl = 8px */
        transition: border-color 150ms ease, box-shadow 150ms ease;
        appearance: none;
        background-image: url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 24 24' fill='none' stroke='%235B606D' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'%3E%3Cpolyline points='6 9 12 15 18 9'%3E%3C/polyline%3E%3C/svg%3E");
        background-repeat: no-repeat;
        background-position: right 14px center;
        background-size: 16px;
        cursor: pointer;
      }

      .sw-select:focus-visible {
        outline: 3px solid var(--softtime);
        outline-offset: 2px;
        border-color: var(--softtime);
      }

      /* ---- Badges de Statut (§8.3) ---- */
      .sw-user-status-badges {
        display: flex;
        align-items: center;
        gap: 8px;
        flex-wrap: wrap;
      }

      .sw-badge {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        padding: 4px 10px;
        font-size: 12px;
        font-weight: 600;
        border-radius: 6px; /* rounded-md = 6px */
        line-height: 1.4;
      }

      .sw-badge-role {
        background-color: var(--surface-light); /* neutral-50 */
        color: var(--app-text); /* neutral-800 */
        border: 1px solid var(--app-border); /* neutral-200 */
      }

      .sw-badge-default {
        background-color: var(--surface-light);
        color: var(--text-secondary); /* neutral-600 */
        border: 1px solid var(--app-border);
      }

      .sw-badge-custom {
        background-color: var(--app-surface-soft); /* navy-50 */
        color: var(--softtime); /* navy-700 */
        border: 1px solid var(--navy-200); /* navy-200 */
      }

      .sw-badge-admin-shield {
        background-color: var(--warning-bg); /* amber-50 Avertissement charte */
        color: var(--warning); /* amber-700 */
        border: 1px solid var(--warning-border); /* amber-200 */
      }

      /* ---- Petits Boutons Secondaires (§8.1 Petit 36px) ---- */
      .sw-bulk-actions {
        display: flex;
        align-items: center;
        gap: 8px;
      }

      .sw-btn-sm {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        min-height: 36px; /* 36px charte */
        padding: 0 14px;
        background-color: var(--app-surface);
        border: 1px solid var(--app-border); /* neutral-200 */
        border-radius: 8px;
        font-size: 13px;
        font-weight: 600;
        color: var(--app-text);
        cursor: pointer;
        transition: all 150ms ease;
      }

      .sw-btn-sm:hover:not(:disabled) {
        background-color: var(--surface-light);
        border-color: var(--border-hover);
      }

      .sw-btn-sm:focus-visible {
        outline: 3px solid var(--softtime);
        outline-offset: 2px;
      }

      .sw-btn-sm:disabled {
        opacity: 0.45;
        cursor: not-allowed;
      }

      /* ---- 3. Cartes de Catégories de Permissions ---- */
      .sw-permissions-container {
        display: flex;
        flex-direction: column;
        gap: 20px;
      }

      .sw-perm-card {
        overflow: hidden;
        transition: box-shadow 150ms ease;
      }

      .sw-perm-card-header {
        display: flex;
        align-items: center;
        justify-content: space-between;
        flex-wrap: wrap;
        gap: 14px;
        padding: 18px 24px;
        background-color: var(--app-surface);
        border-bottom: 1px solid var(--border-light);
      }

      .sw-perm-card.is-collapsed .sw-perm-card-header {
        border-bottom: none;
      }

      .sw-perm-title-group {
        display: flex;
        align-items: center;
        gap: 12px;
      }

      .sw-icon-pill {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 36px;
        height: 36px;
        border-radius: 8px;
        background-color: var(--app-surface-soft); /* navy-50 */
        color: var(--softtime); /* navy-700 Marine Softwell */
        border: 1px solid var(--navy-200);
      }

      .sw-category-title {
        margin: 0;
        font-family: 'Outfit', 'Inter', system-ui, sans-serif;
        font-size: 16px;
        font-weight: 600;
        color: var(--app-text-strong); /* navy-950 */
      }

      .sw-category-desc {
        margin: 2px 0 0;
        font-size: 12px;
        color: var(--text-secondary); /* neutral-600 */
      }

      /* Compteur selon Charte §8.3 (Or clair, texte marine nuit) */
      .sw-counter-badge {
        display: inline-flex;
        align-items: center;
        padding: 2px 8px;
        font-size: 12px;
        font-weight: 700;
        font-family: 'Inter', system-ui, sans-serif;
        font-variant-numeric: tabular-nums;
        border-radius: 6px;
        background-color: var(--color-gold-300); /* gold-300 */
        color: var(--text-on-gold); /* navy-950 - Contraste 8,6:1 conforme AA */
        border: 1px solid var(--color-gold-400);
      }

      .sw-card-bulk-actions {
        display: flex;
        align-items: center;
        gap: 8px;
      }

      .sw-btn-xs {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        height: 30px;
        padding: 0 10px;
        background-color: var(--app-surface);
        border: 1px solid var(--app-border);
        border-radius: 6px;
        font-size: 12px;
        font-weight: 500;
        color: var(--app-text);
        cursor: pointer;
        transition: all 150ms ease;
      }

      .sw-btn-xs:hover:not(:disabled) {
        background-color: var(--surface-light);
        border-color: var(--border-hover);
      }

      .sw-btn-xs:disabled {
        opacity: 0.45;
        cursor: not-allowed;
      }

      .sw-btn-icon-collapse {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 30px;
        height: 30px;
        padding: 0;
        background-color: transparent;
        border: 1px solid var(--app-border);
        border-radius: 6px;
        color: var(--text-secondary);
        cursor: pointer;
        transition: all 150ms ease;
      }

      .sw-btn-icon-collapse:hover {
        background-color: var(--surface-light);
        color: var(--softtime);
      }

      /* ---- Contenu de la Carte & Liste des Lignes ---- */
      .sw-card-content {
        padding: 16px 24px;
        background-color: var(--app-surface);
      }

      .sw-empty-hint {
        padding: 24px;
        text-align: center;
        color: var(--text-muted);
        font-size: 13px;
        font-style: italic;
      }

      .sw-perm-rows-list {
        display: flex;
        flex-direction: column;
        gap: 8px;
      }

      .sw-perm-row {
        display: flex;
        align-items: center;
        justify-content: space-between;
        flex-wrap: wrap;
        gap: 16px;
        padding: 12px 18px;
        background-color: var(--app-bg); /* neutral teinté marine §4.2 */
        border: 1px solid var(--border-light);
        border-radius: 8px; /* rounded-xl = 8px */
        transition: background-color 120ms ease;
      }

      .sw-perm-row:hover {
        background-color: var(--navy-50); /* navy-50 au survol */
        border-color: var(--navy-200);
      }

      .sw-perm-name-col {
        display: flex;
        flex-direction: column;
        min-width: 180px;
      }

      .sw-perm-item-title {
        font-size: 14px;
        font-weight: 600;
        color: var(--app-text-strong); /* navy-950 */
      }

      .sw-perm-item-subtitle {
        font-size: 12px;
        color: var(--text-secondary); /* neutral-600 */
      }

      .sw-actions-toggles {
        display: flex;
        align-items: center;
        gap: 20px;
        flex-wrap: wrap;
      }

      /* ---- Cases à Cocher Stylées & Accessibles (WCAG 2.2 AA) ---- */
      .sw-check-label {
        display: inline-flex;
        align-items: center;
        gap: 8px;
        font-size: 13px;
        font-weight: 500;
        color: var(--app-text);
        cursor: pointer;
        user-select: none;
        min-height: 32px;
      }

      .sw-check-label input[type='checkbox'] {
        width: 16px;
        height: 16px;
        cursor: pointer;
        accent-color: var(--softtime); /* Marine Softwell §4.1 */
        border-radius: 4px;
      }

      .sw-check-label.is-disabled {
        cursor: not-allowed;
        color: var(--text-muted);
      }

      .sw-check-label.is-disabled input[type='checkbox'] {
        cursor: not-allowed;
      }

      .sw-header-check-label {
        margin-right: 8px;
        font-weight: 600;
        color: var(--softtime);
      }

      .sw-item-access-check {
        min-width: 240px;
      }

      .sw-lock-indicator {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        color: var(--warning); /* amber-700 */
        margin-left: 2px;
      }

      .sw-draft-bar {
        position: sticky;
        bottom: 0;
        z-index: 20;
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 16px;
        padding: 12px 24px;
        margin-top: 16px;
        margin-bottom: 16px;
        background: var(--app-surface);
        border-color: var(--app-border);
      }

      .sw-draft-bar-content {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 8px 16px;
      }

      .sw-draft-bar-title {
        font-weight: 600;
        color: var(--app-text-strong);
      }

      .sw-draft-bar-text {
        color: var(--text-secondary);
      }

      .sw-draft-bar-warning {
        color: var(--warning);
        font-weight: 500;
      }

      .sw-sensitive {
        display: inline-flex;
        align-items: center;
        color: var(--warning);
        margin-left: 4px;
      }

      .sw-draft-bar-others {
        color: var(--text-secondary);
        font-style: italic;
      }

      .sw-draft-bar-actions {
        display: flex;
        align-items: center;
        gap: 8px;
      }

      .sw-lock-dot {
        display: inline-block;
        width: 6px;
        height: 6px;
        border-radius: 50%;
        background-color: var(--color-gold-400); /* gold-400 */
        margin-left: 2px;
      }
    `,
  ],
})
export class PrivilegesPage implements OnInit {
  private readonly usersSvc = inject(UsersService);
  private readonly rolesSvc = inject(RolesService);
  private readonly privSvc = inject(PrivilegesService);
  private readonly sageSvc = inject(SageDatabasesService);
  private readonly pointeuseSvc = inject(PointeuseDatabasesService);
  private readonly session = inject(SessionStore);
  private readonly auth = inject(AuthService);
  private readonly toast = inject(ToastService);
  private readonly draft = inject(PrivilegesDraftService);
  private readonly confirmService = inject(ConfirmService);

  readonly users = signal<User[]>([]);
  readonly userOptions = signal<{ value: number; label: string }[]>([]);
  readonly sageDbs = signal<SageDb[]>([]);
  readonly pointeuseDbs = signal<PointeuseDb[]>([]);

  readonly mode = signal<PrivilegesMode>('user');
  readonly selectedUserId = signal<number | null>(null);
  readonly selectedUser = computed(() => {
    const id = this.selectedUserId();
    return this.users().find((u) => u.id === id) ?? null;
  });
  readonly roles = signal<Role[]>([]);
  readonly selectedRoleId = signal<number | null>(null);
  readonly isRoleConfigured = signal(true);
  readonly permissionsLoading = signal(false);
  readonly permissionsLoaded = signal(false);
  private permissionsRequestId = 0;
  readonly roleOptions = computed(() => this.roles().map((role) => ({ value: role.id, label: role.nom })));
  readonly selectedRoleLabel = computed(() => {
    const id = this.selectedRoleId();
    const role = this.roles().find((item) => item.id === id);
    return role?.nom ?? (id ? `Rôle #${id}` : '—');
  });
  readonly hasTarget = computed(() =>
    this.mode() === 'user' ? this.selectedUserId() !== null : this.selectedRoleId() !== null,
  );

  readonly grantedPermissions = signal<Set<string>>(new Set());
  readonly isCustomized = signal(false);

  // Sources des droits (mode « Par utilisateur ») : droits du rôle du compte, chargés une fois par rôle.
  private readonly roleBaseline = signal<{ roleId: number; perms: Set<string> } | null>(null);
  private readonly rolePermsCache = new Map<number, Set<string>>();
  private roleBaselineRequest = 0;
  private readonly currentRolePerms = computed(() => {
    const base = this.roleBaseline();
    const roleId = this.selectedUser()?.roleId;
    return base && roleId !== undefined && base.roleId === roleId ? base.perms : null;
  });
  /** Écarts du brouillon en cours (cases cochées non enregistrées comprises). */
  readonly sources = computed(() => {
    const role = this.currentRolePerms();
    if (this.mode() !== 'user' || this.adminLocked() || !role || !this.permissionsLoaded()) return null;
    return computeSources(role, this.grantedPermissions());
  });
  /** Écarts enregistrés sur le serveur : ce que « Revenir aux droits du rôle » effacerait. */
  private readonly savedSources = computed(() => {
    const role = this.currentRolePerms();
    if (this.mode() !== 'user' || this.adminLocked() || !role || !this.permissionsLoaded()) return null;
    return computeSources(role, this.baseline());
  });
  readonly showCustomized = computed(() => {
    const s = this.sources();
    return s ? s.count > 0 : this.isCustomized();
  });
  readonly badgeLabel = computed(() => {
    if (this.adminLocked()) return 'Tous les droits';
    const s = this.sources();
    if (s) return s.count > 0 ? customizedBadgeLabel(s) : 'Permissions par défaut (Rôle)';
    return this.isCustomized() ? 'Permissions personnalisées' : 'Permissions par défaut (Rôle)';
  });
  readonly canResetToRole = computed(() => (this.savedSources()?.count ?? 0) > 0);

  mark(code: string): 'added' | 'removed' | null {
    const s = this.sources();
    if (!s) return null;
    return s.added.has(code) ? 'added' : s.removed.has(code) ? 'removed' : null;
  }
  readonly saving = signal(false);
  readonly baseline = signal<Set<string>>(new Set());
  readonly loadedTarget = signal<{ mode: PrivilegesMode; id: number } | null>(null);
  readonly serverChangedWarning = signal(false);
  readonly otherDirtyCount = signal(0);

  readonly diff = computed(() => diffPermissions(this.baseline(), this.grantedPermissions()));
  readonly changesCount = computed(() => this.diff().count);
  readonly isDirty = computed(() => this.changesCount() > 0 && !this.permissionsLocked());

  private readonly syncDraftEffect = effect(() => {
    const t = this.loadedTarget();
    const perms = this.grantedPermissions();
    if (!t || !this.permissionsLoaded()) return;
    untracked(() => {
      this.draft.update(t.mode, t.id, perms);
      this.otherDirtyCount.set(
        this.draft.dirtyTargets().filter((d) => !(d.mode === t.mode && d.targetId === t.id)).length,
      );
    });
  });

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
      items: [
        { code: 'REPORTS:REPORTS', label: 'Rapports et exports' },
        { code: 'REPORTS:PREDICTIONS', label: 'Prévisions' },
        { code: 'REPORTS:CLOSING', label: 'Contrôle avant paie' },
      ],
    },
  ];

  /** Pages (codes sans action) : cocher une action coche sa page, décocher une page décoche ses actions. */
  private readonly pageCodes: ReadonlySet<string> = new Set(this.featureCategories.flatMap((c) => c.items.map((i) => i.code)));

  // Recherche : n'agit que sur l'affichage, jamais sur le brouillon.
  readonly search = signal('');
  readonly searching = computed(() => this.search().trim() !== '');
  readonly visibleCategories = computed(() => filterCategories(this.featureCategories, this.search()));
  readonly sageSearchLabels = computed(() => ['Bases RH / Paie', 'Ajouter une base', ...this.sageDbs().map((d) => d.nomBd ?? '')]);
  readonly pointeuseSearchLabels = computed(() => ['Bases de pointage', 'Ajouter une base', ...this.pointeuseDbs().map((d) => d.nomBd ?? '')]);
  readonly matchesAnySearch = matchesAnySearch;
  readonly isSensitive = isSensitiveRight;
  readonly sensitiveHint = SENSITIVE_RIGHT_HINT;

  /** Avertissements du brouillon (ne bloquent pas l'enregistrement). */
  readonly warnings = computed(() =>
    this.adminLocked() ? [] : computeWarnings(this.grantedPermissions(), this.mode(), this.baseline()),
  );

  readonly totalSagePerms = computed(() => (this.sageDbs().length > 0 ? 1 + this.sageDbs().length * 3 : 0));
  readonly totalPointeusePerms = computed(() => (this.pointeuseDbs().length > 0 ? 1 + this.pointeuseDbs().length * 3 : 0));

  totalCategoryPerms(shown: FeatureCategory): number {
    // La catégorie affichée peut être filtrée par la recherche : le total porte toujours sur la catégorie complète.
    const cat = this.featureCategories.find((c) => c.key === shown.key) ?? shown;
    return cat.items.reduce((acc, it) => {
      let count = 1;
      if (it.canCreate) count++;
      if (it.canUpdate) count++;
      if (it.canDelete) count++;
      return acc + count;
    }, 0);
  }

  ngOnInit(): void {
    const active = this.draft.getActive();
    let activeUserId: number | null = null;
    if (active) {
      this.mode.set(active.mode);
      if (active.mode === 'role') {
        this.selectedRoleId.set(active.targetId);
      } else {
        activeUserId = active.targetId;
      }
    }
    this.sageSvc.list().subscribe((dbs) => this.sageDbs.set(dbs));
    this.pointeuseSvc.list().subscribe((dbs) => this.pointeuseDbs.set(dbs));

    this.usersSvc.list().subscribe((users) => {
      this.users.set(users);
      this.userOptions.set(users.map((u) => ({ value: u.id, label: `${u.nom} (${u.login})` })));
      if (users.length) {
        if (this.mode() === 'user') {
          const targetUser = (activeUserId !== null ? users.find((u) => u.id === activeUserId) : null) ?? users[0];
          this.selectedUserId.set(targetUser.id);
          this.loadUserPermissions(targetUser.id);
        } else {
          if (this.selectedUserId() === null) {
            this.selectedUserId.set(users[0].id);
          }
        }

      }
    });

    this.rolesSvc.list().subscribe({
      next: (roles) => {
        this.roles.set(roles);
        if (roles.length) {
          const currentRoleId = this.selectedRoleId();
          const validRole = currentRoleId !== null ? roles.find((r) => r.id === currentRoleId) : null;
          const chosenRoleId = validRole ? validRole.id : roles[0].id;
          this.selectedRoleId.set(chosenRoleId);
          if (this.mode() === 'role') {
            this.loadRolePermissions(chosenRoleId);
          }

        }
      },
      error: () => this.toast.error('Impossible de charger la liste des rôles.'),
    });
  }

  setMode(mode: PrivilegesMode): void {
    if (this.mode() === mode) return;
    this.mode.set(mode);
    if (mode === 'role') {
      const roleId = this.selectedRoleId();
      if (roleId) this.loadRolePermissions(roleId);
    } else {
      const userId = this.selectedUserId();
      if (userId) this.loadUserPermissions(userId);
    }
  }

  onRoleChange(roleId: unknown): void {
    const id = Number(roleId);
    if (!id) return;
    this.selectedRoleId.set(id);
    this.loadRolePermissions(id);
  }

  private loadRolePermissions(roleId: number): void {
    this.draft.setActive('role', roleId);
    const requestId = this.beginPermissionsLoad();
    this.privSvc.getRolePermissions(roleId).subscribe({
      next: (res) => {
        if (requestId !== this.permissionsRequestId) return;
        this.permissionsLoading.set(false);
        this.permissionsLoaded.set(true);

        const r = this.draft.refreshBaseline('role', roleId, res.grantedPermissions ?? []);
        this.baseline.set(r.baseline);
        this.grantedPermissions.set(r.working);
        this.serverChangedWarning.set(r.dirty && r.serverChanged);
        this.loadedTarget.set({ mode: 'role', id: roleId });
        this.isRoleConfigured.set(res.isConfigured);
      },
      error: () => {
        if (requestId !== this.permissionsRequestId) return;
        this.permissionsLoading.set(false);
        this.toast.error('Impossible de charger les privilèges de ce rôle.');
      },
    });
  }

  onUserChange(userId: unknown): void {
    const id = Number(userId);
    if (!id) return;
    this.selectedUserId.set(id);
    this.loadUserPermissions(id);
  }

  private loadUserPermissions(userId: number): void {
    this.draft.setActive('user', userId);
    const requestId = this.beginPermissionsLoad();
    this.privSvc.byUser(userId).subscribe({
      next: (res) => {
        if (requestId !== this.permissionsRequestId) return;
        this.permissionsLoading.set(false);
        this.permissionsLoaded.set(true);

        const r = this.draft.refreshBaseline('user', userId, res.grantedPermissions ?? []);
        this.baseline.set(r.baseline);
        this.grantedPermissions.set(r.working);
        this.serverChangedWarning.set(r.dirty && r.serverChanged);
        this.loadedTarget.set({ mode: 'user', id: userId });
        this.isCustomized.set(res.isCustomized);
        this.loadRoleBaseline(this.users().find((u) => u.id === userId)?.roleId);
      },
      error: () => {
        if (requestId !== this.permissionsRequestId) return;
        this.permissionsLoading.set(false);
        this.toast.error('Impossible de charger les privilèges de cet utilisateur.');
      },
    });
  }

  /** Droits du rôle du compte choisi : un appel par rôle (puis cache), sans recharger à chaque case cochée. */
  private loadRoleBaseline(roleId: number | undefined): void {
    if (roleId === undefined) return;
    const cached = this.rolePermsCache.get(roleId);
    if (cached) {
      this.roleBaseline.set({ roleId, perms: cached });
      return;
    }
    const request = ++this.roleBaselineRequest;
    this.privSvc.getRolePermissions(roleId).subscribe({
      next: (res) => {
        if (request !== this.roleBaselineRequest) return;
        const perms = new Set(res.grantedPermissions ?? []);
        this.rolePermsCache.set(roleId, perms);
        this.roleBaseline.set({ roleId, perms });
      },
      // Échec : la page fonctionne sans marqueurs ni compteur d'écarts (ancien badge).
      error: () => {
        if (request === this.roleBaselineRequest) this.roleBaseline.set(null);
      },
    });
  }

  async resetToRole(): Promise<void> {
    const userId = this.selectedUserId();
    const user = this.selectedUser();
    const saved = this.savedSources();
    if (userId === null || !user || !saved || saved.count === 0 || this.adminLocked()) return;
    const message = buildResetToRoleMessage(this.getRoleLabel(user.roleId), saved.count, this.draft.hasDirty('user', userId));
    if (!(await this.confirmService.ask(message, 'Revenir aux droits du rôle', 'Revenir au rôle'))) return;

    this.saving.set(true);
    this.privSvc.resetUserPermissions(userId).subscribe({
      next: () => {
        this.saving.set(false);
        this.draft.clearTarget('user', userId);
        this.loadUserPermissions(userId);
        this.toast.success(`Droits du rôle rétablis pour ${user.nom} (${user.login}). ${appliesAtNextLoginNote('user')}`);
      },
      error: (e: unknown) => {
        this.saving.set(false);
        this.toast.error(extractErrorMessage(e));
      },
    });
  }

  private beginPermissionsLoad(): number {
    this.permissionsRequestId += 1;
    this.permissionsLoading.set(true);
    this.permissionsLoaded.set(false);
    this.grantedPermissions.set(new Set());
    this.isCustomized.set(false);
    this.isRoleConfigured.set(true);
    this.loadedTarget.set(null);
    this.baseline.set(new Set());
    this.serverChangedWarning.set(false);
    return this.permissionsRequestId;
  }

  readonly adminLocked = computed(() =>
    this.mode() === 'role' ? isAdminRole(this.selectedRoleId()) : isAdminRole(this.selectedUser()?.roleId),
  );
  readonly roleEditLocked = computed(
    () => !this.adminLocked() && this.mode() === 'role' && !canEditRolePermissions(this.session.session()?.roleId, this.selectedRoleId()),
  );
  readonly permissionsLocked = computed(
    () => this.adminLocked() || this.roleEditLocked() || this.permissionsLoading() || !this.permissionsLoaded(),
  );
  readonly isTargetAdmin = this.adminLocked;

  isReadOnly(code: string): boolean {
    return this.permissionsLocked() || this.isMandatory(code);
  }

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
    if (this.adminLocked()) return true;
    if (this.isMandatory(code)) return true;
    return this.grantedPermissions().has(code);
  }

  toggle(code: string, event: Event): void {
    if (this.permissionsLocked()) return;
    if (this.isMandatory(code)) {
      (event.target as HTMLInputElement).checked = true;
      this.toast.info('Cette permission est obligatoire pour un compte administrateur.');
      return;
    }
    const checked = (event.target as HTMLInputElement).checked;
    this.grantedPermissions.update((set) => applyToggle(set, code, checked, this.pageCodes, (c) => this.isMandatory(c)));
  }

  readonly collapsedSections = signal<Set<string>>(
    new Set(['SAGE', 'POINTEUSE', 'PARAMS', 'PROC', 'OTHER', 'TRACE', 'REPORTS']),
  );

  isCollapsed(key: string): boolean {
    // Pendant une recherche, les catégories affichées sont ouvertes (sans modifier l'état de repli mémorisé).
    return !this.searching() && this.collapsedSections().has(key);
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
    if (this.permissionsLocked()) return;
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
    if (this.permissionsLocked()) return;
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
    if (this.permissionsLocked()) return;
    if (this.mode() === 'role') {
      this.saveRole();
      return;
    }
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
    const requestId = this.permissionsRequestId;
    const target = this.selectedUser();
    const targetName = target ? `${target.nom} (${target.login})` : `Utilisateur #${userId}`;
    this.privSvc.saveUserPermissions(userId, perms).subscribe({
      next: () => {
        this.draft.markSaved('user', userId, perms);
        this.saving.set(false);
        if (this.saveTargetChanged('user', userId, requestId)) {
          this.toast.success(`Droits enregistrés pour ${targetName}. ` + appliesAtNextLoginNote('user'));
          return;
        }
        this.baseline.set(new Set(perms));
        this.serverChangedWarning.set(false);
        this.isCustomized.set(true);
        if (currentSessionUser && currentSessionUser.userId === userId) {
          this.session.setPermissions(perms, true);
          this.auth.refreshMe().subscribe();
        }
        this.toast.success('Permissions enregistrées avec succès. ' + appliesAtNextLoginNote('user'));
      },
      error: (e) => {
        this.saving.set(false);
        if (this.saveTargetChanged('user', userId, requestId)) {
          this.toast.error(`Échec pour ${targetName} : ${extractErrorMessage(e)}`);
          return;
        }
        const err = e?.error?.message || e?.error?.title || e?.error || "Erreur lors de l'enregistrement.";
        this.toast.error(err);
      },
    });
  }

  private saveTargetChanged(mode: PrivilegesMode, targetId: number, requestId: number): boolean {
    if (this.permissionsRequestId !== requestId || this.mode() !== mode) return true;
    return mode === 'role' ? this.selectedRoleId() !== targetId : this.selectedUserId() !== targetId;
  }

  private saveRole(): void {
    const roleId = this.selectedRoleId();
    if (!roleId) return;
    if (!canEditRolePermissions(this.session.session()?.roleId, roleId)) return;
    if (isAdminRole(roleId)) return;

    if (this.users().length === 0) {
      this.toast.error("Liste des utilisateurs indisponible : impossible de calculer l'impact du changement.");
      return;
    }

    const count = countUsersOfRole(this.users(), roleId);
    if (!window.confirm(`${buildRoleImpactMessage(count)}\n\nConfirmer l'enregistrement ?`)) return;

    this.saving.set(true);
    const perms = Array.from(this.grantedPermissions());
    const requestId = this.permissionsRequestId;
    const targetName = this.selectedRoleLabel();
    this.privSvc.saveRolePermissions(roleId, perms).subscribe({
      next: () => {
        this.draft.markSaved('role', roleId, perms);
        this.saving.set(false);
        if (this.saveTargetChanged('role', roleId, requestId)) {
          this.toast.success(`Droits enregistrés pour ${targetName}. ` + appliesAtNextLoginNote('role'));
          return;
        }
        this.baseline.set(new Set(perms));
        this.serverChangedWarning.set(false);
        this.rolePermsCache.delete(roleId);
        this.loadRolePermissions(roleId);
        this.toast.success('Droits du rôle enregistrés. ' + appliesAtNextLoginNote('role'));
      },
      error: (err: unknown) => {
        this.saving.set(false);
        if (this.saveTargetChanged('role', roleId, requestId)) {
          this.toast.error(`Échec pour ${targetName} : ${extractErrorMessage(err)}`);
          return;
        }
        this.toast.error(extractErrorMessage(err));
      },
    });
  }

  @HostListener('window:beforeunload', ['$event'])
  beforeUnload(event: BeforeUnloadEvent): void {
    if (this.draft.hasDirty()) {
      event.preventDefault();
      event.returnValue = '';
    }
  }

  discardChanges(): void {
    if (!this.isDirty()) return;
    const target = this.loadedTarget();
    if (!target) return;
    if (!window.confirm('Annuler les modifications non enregistrées ?')) return;
    this.draft.discard(target.mode, target.id);
    this.grantedPermissions.set(new Set(this.baseline()));
    this.serverChangedWarning.set(false);
  }

  getRoleLabel(roleId: number): string {
    return resolveRoleLabel(this.roles(), roleId);
  }
}
