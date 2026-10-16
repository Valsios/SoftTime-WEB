import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ToastService } from '../../core/toast.service';
import { SessionStore } from '../../core/session.store';
import { PointeuseDb, SageDb, User } from '../../shared/models';
import { PointeuseDatabasesService } from '../../shared/services/catalog.service';
import { PrivilegesService, SageDatabasesService, UsersService } from '../../shared/services/users.service';
import { AuthService } from '../../shared/services/auth.service';
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
  imports: [CommonModule, FormsModule, Icon],
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
          [disabled]="saving() || !selectedUserId()"
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

    <!-- Carte Sélecteur d'utilisateur - Conforme Charte Softwell §7.1 N°5 & §8.4 -->
    <section class="sw-card sw-user-select-card" aria-label="Sélection de l'utilisateur">
      <div class="sw-user-selector-bar">
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
          <span class="sw-badge" [class.sw-badge-custom]="isCustomized()" [class.sw-badge-default]="!isCustomized()">
            {{ isCustomized() ? 'Permissions personnalisées' : 'Permissions par défaut (Rôle)' }}
          </span>
          @if (isTargetAdmin()) {
            <span class="sw-badge sw-badge-admin-shield" title="Règle d'inviolabilité : un administrateur conserve l'accès complet à toutes les bases et fonctionnalités vitales.">
              <app-icon name="shield" [size]="14"></app-icon>
              Bases et gestion verrouillées
            </span>
          }
        </div>

        <div class="sw-bulk-actions">
          <button type="button" class="sw-btn-sm" (click)="toggleAll(true)">
            Tout cocher
          </button>
          <button
            type="button"
            class="sw-btn-sm"
            [disabled]="isTargetAdmin()"
            (click)="toggleAll(false)"
            [title]="isTargetAdmin() ? 'Les bases et droits cles restent obligatoires pour un administrateur' : 'Tout decocher'"
          >
            Tout décocher
          </button>
        </div>
      </div>
    </section>

    <!-- Conteneur des permissions par catégorie -->
    <div class="sw-permissions-container" *ngIf="selectedUserId">
      <!-- Section 1 : Bases RH / Paie (Charte §7.1 N°8 & §8.4) -->
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
            <label class="sw-check-label sw-header-check-label" [class.is-disabled]="isMandatory('SAGE:CREATE')">
              <input
                type="checkbox"
                [checked]="has('SAGE:CREATE')"
                [disabled]="isMandatory('SAGE:CREATE')"
                (change)="toggle('SAGE:CREATE', $event)"
              />
              <span>Ajouter une base</span>
              @if (isMandatory('SAGE:CREATE')) {
                <span class="sw-lock-indicator" title="Obligatoire pour le profil Administrateur">
                  <app-icon name="shield" [size]="12"></app-icon>
                </span>
              }
            </label>
            <button type="button" class="sw-btn-xs" (click)="toggleGroup('SAGE', true)">Tout cocher</button>
            <button type="button" class="sw-btn-xs" (click)="toggleGroup('SAGE', false)" [disabled]="isTargetAdmin()">Tout décocher</button>
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
                      <label class="sw-check-label" [class.is-disabled]="isMandatory('SAGE:' + db.nomBd + ':READ')">
                        <input
                          type="checkbox"
                          [checked]="has('SAGE:' + db.nomBd + ':READ')"
                          [disabled]="isMandatory('SAGE:' + db.nomBd + ':READ')"
                          (change)="toggle('SAGE:' + db.nomBd + ':READ', $event)"
                        />
                        <span>Consulter</span>
                        @if (isMandatory('SAGE:' + db.nomBd + ':READ')) {
                          <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                        }
                      </label>

                      <label class="sw-check-label" [class.is-disabled]="isMandatory('SAGE:' + db.nomBd + ':UPDATE')">
                        <input
                          type="checkbox"
                          [checked]="has('SAGE:' + db.nomBd + ':UPDATE')"
                          [disabled]="isMandatory('SAGE:' + db.nomBd + ':UPDATE')"
                          (change)="toggle('SAGE:' + db.nomBd + ':UPDATE', $event)"
                        />
                        <span>Modifier</span>
                        @if (isMandatory('SAGE:' + db.nomBd + ':UPDATE')) {
                          <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                        }
                      </label>

                      <label class="sw-check-label" [class.is-disabled]="isMandatory('SAGE:' + db.nomBd + ':DELETE')">
                        <input
                          type="checkbox"
                          [checked]="has('SAGE:' + db.nomBd + ':DELETE')"
                          [disabled]="isMandatory('SAGE:' + db.nomBd + ':DELETE')"
                          (change)="toggle('SAGE:' + db.nomBd + ':DELETE', $event)"
                        />
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

      <!-- Section 2 : Bases de pointage (Charte §7.1 N°8 & §8.4) -->
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
            <label class="sw-check-label sw-header-check-label" [class.is-disabled]="isMandatory('POINTEUSE:CREATE')">
              <input
                type="checkbox"
                [checked]="has('POINTEUSE:CREATE')"
                [disabled]="isMandatory('POINTEUSE:CREATE')"
                (change)="toggle('POINTEUSE:CREATE', $event)"
              />
              <span>Ajouter une base</span>
              @if (isMandatory('POINTEUSE:CREATE')) {
                <span class="sw-lock-indicator" title="Obligatoire pour le profil Administrateur">
                  <app-icon name="shield" [size]="12"></app-icon>
                </span>
              }
            </label>
            <button type="button" class="sw-btn-xs" (click)="toggleGroup('POINTEUSE', true)">Tout cocher</button>
            <button type="button" class="sw-btn-xs" (click)="toggleGroup('POINTEUSE', false)" [disabled]="isTargetAdmin()">Tout décocher</button>
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
                      <label class="sw-check-label" [class.is-disabled]="isMandatory('POINTEUSE:' + db.nomBd + ':READ')">
                        <input
                          type="checkbox"
                          [checked]="has('POINTEUSE:' + db.nomBd + ':READ')"
                          [disabled]="isMandatory('POINTEUSE:' + db.nomBd + ':READ')"
                          (change)="toggle('POINTEUSE:' + db.nomBd + ':READ', $event)"
                        />
                        <span>Consulter</span>
                        @if (isMandatory('POINTEUSE:' + db.nomBd + ':READ')) {
                          <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                        }
                      </label>

                      <label class="sw-check-label" [class.is-disabled]="isMandatory('POINTEUSE:' + db.nomBd + ':UPDATE')">
                        <input
                          type="checkbox"
                          [checked]="has('POINTEUSE:' + db.nomBd + ':UPDATE')"
                          [disabled]="isMandatory('POINTEUSE:' + db.nomBd + ':UPDATE')"
                          (change)="toggle('POINTEUSE:' + db.nomBd + ':UPDATE', $event)"
                        />
                        <span>Modifier</span>
                        @if (isMandatory('POINTEUSE:' + db.nomBd + ':UPDATE')) {
                          <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                        }
                      </label>

                      <label class="sw-check-label" [class.is-disabled]="isMandatory('POINTEUSE:' + db.nomBd + ':DELETE')">
                        <input
                          type="checkbox"
                          [checked]="has('POINTEUSE:' + db.nomBd + ':DELETE')"
                          [disabled]="isMandatory('POINTEUSE:' + db.nomBd + ':DELETE')"
                          (change)="toggle('POINTEUSE:' + db.nomBd + ':DELETE', $event)"
                        />
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

      <!-- Sections 3 à 7 : Catégories fonctionnelles (Charte §7.1 & §8.4) -->
      @for (cat of featureCategories; track cat.key) {
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
              <button type="button" class="sw-btn-xs" (click)="toggleGroup(cat.key, true)">Tout cocher</button>
              <button type="button" class="sw-btn-xs" (click)="toggleGroup(cat.key, false)">Tout décocher</button>
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
                    <label class="sw-check-label sw-item-access-check" [class.is-disabled]="isMandatory(item.code)">
                      <input
                        type="checkbox"
                        [checked]="has(item.code)"
                        [disabled]="isMandatory(item.code)"
                        (change)="toggle(item.code, $event)"
                      />
                      <strong class="sw-perm-item-title">{{ item.label }}</strong>
                      @if (isMandatory(item.code)) {
                        <span class="sw-lock-indicator" title="Obligatoire pour le profil Administrateur">
                          <app-icon name="shield" [size]="12"></app-icon>
                        </span>
                      }
                    </label>

                    @if (item.canCreate || item.canUpdate || item.canDelete) {
                      <div class="sw-actions-toggles">
                        @if (item.canCreate) {
                          <label class="sw-check-label" [class.is-disabled]="isMandatory(item.code + ':CREATE')">
                            <input
                              type="checkbox"
                              [checked]="has(item.code + ':CREATE')"
                              [disabled]="isMandatory(item.code + ':CREATE')"
                              (change)="toggle(item.code + ':CREATE', $event)"
                            />
                            <span>Ajouter</span>
                            @if (isMandatory(item.code + ':CREATE')) {
                              <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                            }
                          </label>
                        }
                        @if (item.canUpdate) {
                          <label class="sw-check-label" [class.is-disabled]="isMandatory(item.code + ':UPDATE')">
                            <input
                              type="checkbox"
                              [checked]="has(item.code + ':UPDATE')"
                              [disabled]="isMandatory(item.code + ':UPDATE')"
                              (change)="toggle(item.code + ':UPDATE', $event)"
                            />
                            <span>Modifier</span>
                            @if (isMandatory(item.code + ':UPDATE')) {
                              <span class="sw-lock-dot" title="Obligatoire pour l'administrateur"></span>
                            }
                          </label>
                        }
                        @if (item.canDelete) {
                          <label class="sw-check-label" [class.is-disabled]="isMandatory(item.code + ':DELETE')">
                            <input
                              type="checkbox"
                              [checked]="has(item.code + ':DELETE')"
                              [disabled]="isMandatory(item.code + ':DELETE')"
                              (change)="toggle(item.code + ':DELETE', $event)"
                            />
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
         Palette : Navy-700 (#2E427B), Navy-800 (#243569), Navy-950 (#0B1332)
                   Gold-400 (#AA9E39), Gold-300 (#E1C340), Line-input (#878D9A)
         Typographie : Outfit (titres) & Inter (corps, boutons, libellés)
         ========================================================================== */

      :host {
        display: block;
        color: #353944; /* neutral-800 */
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
        border-bottom: 1px solid #D6D9E0; /* neutral-200 */
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
        background-color: #AA9E39; /* gold-400 - filet or de la charte */
        border-radius: 1px;
      }

      .sw-eyebrow-text {
        font-size: 12px;
        font-weight: 600;
        letter-spacing: 0.08em;
        text-transform: uppercase;
        color: #5B606D; /* neutral-600 */
      }

      .sw-page-title {
        margin: 0;
        font-family: 'Outfit', 'Inter', system-ui, sans-serif;
        font-size: 24px;
        font-weight: 600;
        line-height: 1.25;
        letter-spacing: -0.02em;
        color: #0B1332; /* navy-950 */
      }

      .sw-page-subtitle {
        margin: 0;
        font-size: 14px;
        line-height: 1.5;
        color: #5B606D; /* neutral-600 */
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
        background-color: #2E427B; /* navy-700 Marine Softwell */
        color: #ffffff;
        font-size: 14px;
        font-weight: 700;
        font-family: 'Inter', system-ui, sans-serif;
        border: none;
        border-radius: 8px; /* rounded-xl charte = 8px */
        box-shadow: 0 1px 2px rgba(11, 19, 50, 0.05);
        cursor: pointer;
        transition: background-color 150ms cubic-bezier(0.2, 0, 0, 1), transform 150ms ease;
        white-space: nowrap;
      }

      .sw-btn-primary:hover:not(:disabled) {
        background-color: #243569; /* navy-800 survol */
      }

      .sw-btn-primary:focus-visible {
        outline: 3px solid #2E427B;
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
        border: 2px solid rgba(255, 255, 255, 0.35);
        border-top-color: #ffffff;
        border-radius: 50%;
        animation: sw-spin 0.6s linear infinite;
      }

      @keyframes sw-spin {
        to { transform: rotate(360deg); }
      }

      /* ---- 2. Carte (§8.4) & Sélecteur d'utilisateur ---- */
      .sw-card {
        background-color: #ffffff;
        border: 1px solid #D6D9E0; /* neutral-200 */
        border-radius: 12px; /* rounded-2xl charte = 12px */
        box-shadow: 0 1px 2px rgba(11, 19, 50, 0.04);
      }

      .sw-user-select-card {
        padding: 20px 24px;
        margin-bottom: 24px;
      }

      .sw-user-selector-bar {
        display: flex;
        align-items: flex-end;
        justify-content: space-between;
        flex-wrap: wrap;
        gap: 20px;
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
        color: #353944; /* neutral-800 casse de phrase */
        margin-bottom: 6px;
      }

      .sw-req {
        color: #BE123C; /* rose-700 / danger */
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
        color: #242830; /* neutral-900 */
        background-color: #ffffff;
        border: 1px solid #878D9A; /* line-input 3,3:1 conforme AA */
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
        outline: 3px solid #2E427B;
        outline-offset: 2px;
        border-color: #2E427B;
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
        background-color: #F5F7F9; /* neutral-50 */
        color: #353944; /* neutral-800 */
        border: 1px solid #D6D9E0; /* neutral-200 */
      }

      .sw-badge-default {
        background-color: #F5F7F9;
        color: #5B606D; /* neutral-600 */
        border: 1px solid #D6D9E0;
      }

      .sw-badge-custom {
        background-color: #F4F7FE; /* navy-50 */
        color: #2E427B; /* navy-700 */
        border: 1px solid #CAD9FB; /* navy-200 */
      }

      .sw-badge-admin-shield {
        background-color: #FFFBEB; /* amber-50 Avertissement charte */
        color: #B45309; /* amber-700 */
        border: 1px solid #FDE68A; /* amber-200 */
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
        background-color: #ffffff;
        border: 1px solid #D6D9E0; /* neutral-200 */
        border-radius: 8px;
        font-size: 13px;
        font-weight: 600;
        color: #353944;
        cursor: pointer;
        transition: all 150ms ease;
      }

      .sw-btn-sm:hover:not(:disabled) {
        background-color: #F5F7F9;
        border-color: #B9BEC8;
      }

      .sw-btn-sm:focus-visible {
        outline: 3px solid #2E427B;
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
        background-color: #ffffff;
        border-bottom: 1px solid #E9EBF0;
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
        background-color: #F4F7FE; /* navy-50 */
        color: #2E427B; /* navy-700 Marine Softwell */
        border: 1px solid #CAD9FB;
      }

      .sw-category-title {
        margin: 0;
        font-family: 'Outfit', 'Inter', system-ui, sans-serif;
        font-size: 16px;
        font-weight: 600;
        color: #0B1332; /* navy-950 */
      }

      .sw-category-desc {
        margin: 2px 0 0;
        font-size: 12px;
        color: #5B606D; /* neutral-600 */
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
        background-color: #E1C340; /* gold-300 */
        color: #0B1332; /* navy-950 - Contraste 8,6:1 conforme AA */
        border: 1px solid #AA9E39;
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
        background-color: #ffffff;
        border: 1px solid #D6D9E0;
        border-radius: 6px;
        font-size: 12px;
        font-weight: 500;
        color: #353944;
        cursor: pointer;
        transition: all 150ms ease;
      }

      .sw-btn-xs:hover:not(:disabled) {
        background-color: #F5F7F9;
        border-color: #B9BEC8;
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
        border: 1px solid #D6D9E0;
        border-radius: 6px;
        color: #5B606D;
        cursor: pointer;
        transition: all 150ms ease;
      }

      .sw-btn-icon-collapse:hover {
        background-color: #F5F7F9;
        color: #2E427B;
      }

      /* ---- Contenu de la Carte & Liste des Lignes ---- */
      .sw-card-content {
        padding: 16px 24px;
        background-color: #ffffff;
      }

      .sw-empty-hint {
        padding: 24px;
        text-align: center;
        color: #747A88;
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
        background-color: #F8FAFD; /* neutral teinté marine §4.2 */
        border: 1px solid #E9EBF0;
        border-radius: 8px; /* rounded-xl = 8px */
        transition: background-color 120ms ease;
      }

      .sw-perm-row:hover {
        background-color: #F4F7FE; /* navy-50 au survol */
        border-color: #CAD9FB;
      }

      .sw-perm-name-col {
        display: flex;
        flex-direction: column;
        min-width: 180px;
      }

      .sw-perm-item-title {
        font-size: 14px;
        font-weight: 600;
        color: #0B1332; /* navy-950 */
      }

      .sw-perm-item-subtitle {
        font-size: 12px;
        color: #5B606D; /* neutral-600 */
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
        color: #353944;
        cursor: pointer;
        user-select: none;
        min-height: 32px;
      }

      .sw-check-label input[type='checkbox'] {
        width: 16px;
        height: 16px;
        cursor: pointer;
        accent-color: #2E427B; /* Marine Softwell §4.1 */
        border-radius: 4px;
      }

      .sw-check-label.is-disabled {
        cursor: not-allowed;
        color: #747A88;
      }

      .sw-check-label.is-disabled input[type='checkbox'] {
        cursor: not-allowed;
      }

      .sw-header-check-label {
        margin-right: 8px;
        font-weight: 600;
        color: #2E427B;
      }

      .sw-item-access-check {
        min-width: 240px;
      }

      .sw-lock-indicator {
        display: inline-flex;
        align-items: center;
        justify-content: center;
        color: #B45309; /* amber-700 */
        margin-left: 2px;
      }

      .sw-lock-dot {
        display: inline-block;
        width: 6px;
        height: 6px;
        border-radius: 50%;
        background-color: #AA9E39; /* gold-400 */
        margin-left: 2px;
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
          this.session.setPermissions(perms, true);
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
