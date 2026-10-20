import { ChangeDetectionStrategy, Component, computed, HostListener, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SessionStore } from '../core/session.store';
import { Droit } from '../core/rights';
import { AuthService } from '../shared/services/auth.service';
import { PointeuseDatabasesService } from '../shared/services/catalog.service';
import { SageDatabasesService } from '../shared/services/users.service';
import { NAV_SECTIONS, NavSection } from './nav';
import { Icon } from '../shared/components/icon';
import { PointeuseDb } from '../shared/models';
import { ToastService } from '../core/toast.service';
import { ConfirmService } from '../core/confirm.service';

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon],
  template: `
    <div class="shell" [class.shell--mini]="collapsed()">
      @if (isMobileViewport() && mobileMenuOpen()) {
        <button class="mobile-nav-overlay" type="button" aria-label="Fermer le menu de navigation" (click)="closeMobileNavigation()"></button>
      }

      <aside
        id="main-navigation"
        class="sidebar softtime-sidebar"
        [class.sidebar--mobile-open]="mobileMenuOpen()"
        [attr.aria-hidden]="isMobileViewport() && !mobileMenuOpen() ? 'true' : null"
        [attr.inert]="isMobileViewport() && !mobileMenuOpen() ? '' : null"
      >
        <div class="sidebar__brand">
          <img class="sidebar__logo" src="assets/SoftTimeLogo.png" alt="Soft Time" />
          @if (!collapsed()) {
            <div class="sidebar__brand-text">
              <strong>Soft Time</strong>
              <small>Gestion des temps</small>
            </div>
          }
        </div>

        <nav class="sidebar__nav">
          <a
            routerLink="/dashboard"
            routerLinkActive="is-active"
            class="sidebar__item"
            [title]="'Tableau de bord'"
            (click)="closeMobileNavigation()"
          >
            <app-icon name="home"></app-icon>
            @if (!collapsed()) {<span>Tableau de bord</span>}
          </a>

          @for (section of visibleSections(); track section.title; let sectionIndex = $index) {
            <div class="sidebar__section">
              @if (!collapsed()) {
                <button
                  class="sidebar__section-toggle"
                  type="button"
                  [attr.aria-expanded]="isSectionExpanded(section.title)"
                  [attr.aria-controls]="'navigation-section-' + sectionIndex"
                  (click)="toggleSection(section.title)"
                >
                  <span class="sidebar__section-title">{{ section.title }}</span>
                  <app-icon [name]="isSectionExpanded(section.title) ? 'chevron-up' : 'chevron-down'" [size]="16"></app-icon>
                </button>
              }
              @if (collapsed() || isSectionExpanded(section.title)) {
                <div class="sidebar__section-items" [id]="'navigation-section-' + sectionIndex">
                  @for (item of section.items; track item.path) {
                    <a
                      [routerLink]="item.path"
                      routerLinkActive="is-active"
                      class="sidebar__item"
                      [title]="item.label"
                      (click)="closeMobileNavigation()"
                    >
                      <app-icon [name]="item.icon"></app-icon>
                      @if (!collapsed()) {<span>{{ item.label }}</span>}
                    </a>
                  }
                </div>
              }
            </div>
          }
        </nav>
      </aside>

      <div class="main">
        <header class="topbar">
          <button
            class="topbar__toggle"
            type="button"
            aria-controls="main-navigation"
            [attr.aria-expanded]="isMobileViewport() ? mobileMenuOpen() : !collapsed()"
            [attr.aria-label]="isMobileViewport() ? (mobileMenuOpen() ? 'Fermer le menu de navigation' : 'Ouvrir le menu de navigation') : 'Réduire ou développer le menu de navigation'"
            (click)="toggleNavigation()"
          >
            <app-icon name="grid" [size]="18"></app-icon>
          </button>

          <img class="topbar__mobile-logo" src="assets/SoftTimeLogo.png" alt="Soft Time" />

          <div class="topbar__spacer"></div>

          <div class="topbar__company" [class.topbar__company--empty]="!databases().length" [title]="databases().length ? 'Société / base SAGE' : 'Aucune base RH / paie configurée ou autorisée'">
            <app-icon name="server" [size]="16"></app-icon>
            <select
              (change)="onCompanyChange($event)"
              aria-label="Société / base SAGE"
              [disabled]="!databases().length"
            >
              @if (!databases().length) {
                <option value="">Aucune base RH</option>
              } @else {
                @for (db of databases(); track db.id) {
                  <option [value]="db.nomBd" [selected]="db.nomBd === session.activeSageDb()">{{ db.nomBd }}</option>
                }
              }
            </select>
          </div>

          <div class="topbar__company" [class.topbar__company--empty]="!pointeuseDbs().length" [title]="pointeuseDbs().length ? 'Base de pointage' : 'Aucune base de pointage configurée'">
            <app-icon name="clock" [size]="16"></app-icon>
            <select
              (change)="onPointeuseChange($event)"
              aria-label="Base de pointage"
              [disabled]="!pointeuseDbs().length"
            >
              @if (!pointeuseDbs().length) {
                <option value="">Aucune pointeuse</option>
              } @else {
                @for (db of pointeuseDbs(); track db.id) {
                  <option [value]="db.nomBd" [selected]="db.nomBd === session.activePointeuseDb()">{{ db.nomBd }}</option>
                }
              }
            </select>
          </div>

          <button
            class="topbar__icon-btn"
            type="button"
            (click)="session.toggleTheme()"
            [title]="session.theme() === 'dark' ? 'Mode clair' : 'Mode sombre'"
          >
            <app-icon [name]="session.theme() === 'dark' ? 'sun' : 'moon'" [size]="18"></app-icon>
          </button>

          <div class="topbar__user">
            <div class="topbar__avatar">{{ initials() }}</div>
            <div class="topbar__user-info">
              <strong>{{ session.displayName() }}</strong>
              <small>{{ session.session()?.matricule || 'Utilisateur' }}</small>
            </div>
          </div>

          <button class="topbar__icon-btn" type="button" (click)="logout()" title="Déconnexion">
            <app-icon name="logout" [size]="18"></app-icon>
          </button>
        </header>

        <main class="content soft-fade-in">
          <router-outlet></router-outlet>
        </main>
      </div>
    </div>
  `,
  styles: [
    `
      .shell {
        display: grid;
        grid-template-columns: var(--sidebar-width) 1fr;
        height: 100vh;
        overflow: hidden;
        transition: grid-template-columns var(--t-normal) var(--ease);
      }
      .shell--mini {
        grid-template-columns: var(--sidebar-mini-width) 1fr;
      }
      .sidebar {
        background: var(--sidebar-bg);
        color: var(--sidebar-text);
        display: flex;
        flex-direction: column;
        overflow-y: auto;
        overflow-x: hidden;
        border-right: 1px solid rgba(0, 0, 0, 0.15);
        transition: transform var(--t-normal) var(--ease);
      }
      .sidebar__brand {
        display: flex;
        align-items: center;
        gap: 12px;
        padding: 20px 18px;
        border-bottom: 1px solid rgba(255, 255, 255, 0.08);
        min-height: 68px;
      }
      .sidebar__logo {
        width: 36px;
        height: 36px;
        object-fit: contain;
        flex: none;
      }
      .sidebar__brand-text {
        display: flex;
        flex-direction: column;
        line-height: 1.2;
      }
      .sidebar__brand-text strong {
        color: #ffffff;
        font-family: var(--font-heading);
        font-size: 16px;
        font-weight: 700;
        letter-spacing: -0.01em;
      }
      .sidebar__brand-text small {
        color: rgba(255, 255, 255, 0.6);
        font-size: 11px;
        font-weight: 500;
        letter-spacing: 0.02em;
      }
      .sidebar__nav {
        padding: 12px 10px;
        display: flex;
        flex-direction: column;
        gap: 4px;
      }
      .sidebar__section {
        margin-top: 16px;
        display: flex;
        flex-direction: column;
        gap: 3px;
      }
      .sidebar__section-toggle {
        width: 100%;
        display: flex;
        align-items: center;
        justify-content: space-between;
        gap: 8px;
        padding: 6px 12px 4px;
        border: 0;
        background: transparent;
        color: var(--sidebar-text-muted);
        cursor: pointer;
        text-align: left;
        border-radius: var(--radius-sm);
      }
      .sidebar__section-toggle:hover {
        color: var(--sidebar-text);
        background: var(--sidebar-hover);
      }
      .sidebar__section-toggle:focus-visible {
        outline: 2px solid var(--gold-300);
        outline-offset: 2px;
      }
      .sidebar__section-title {
        font-size: 11px;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.08em;
      }
      .sidebar__section-items {
        display: flex;
        flex-direction: column;
        gap: 3px;
      }
      .sidebar__item {
        display: flex;
        align-items: center;
        gap: 12px;
        min-height: 44px;
        padding: 10px 14px;
        border-radius: var(--radius-md);
        color: var(--sidebar-text);
        font-size: 13.5px;
        font-weight: 500;
        white-space: nowrap;
        transition: background var(--t-fast) var(--ease), color var(--t-fast) var(--ease);
      }
      .sidebar__item:hover {
        background: var(--sidebar-hover);
        color: #ffffff;
      }
      .sidebar__item.is-active {
        background: var(--sidebar-active-bg);
        color: var(--sidebar-active-color);
        border-left: 3px solid var(--sidebar-active-color);
        border-top-left-radius: 0;
        border-bottom-left-radius: 0;
        font-weight: 600;
      }
      .sidebar__item.is-active app-icon {
        color: var(--sidebar-active-color);
      }
      .main {
        display: flex;
        flex-direction: column;
        overflow: hidden;
        background: var(--app-bg);
      }
      .topbar {
        min-height: 68px;
        display: flex;
        align-items: center;
        gap: 14px;
        padding: 0 24px;
        background: var(--app-surface);
        border-bottom: 1px solid var(--app-border);
        box-shadow: var(--shadow-xs);
        z-index: var(--z-sticky);
      }
      :root[data-theme='dark'] .topbar {
        background: var(--app-surface);
      }
      .topbar__spacer {
        flex: 1;
      }
      .topbar__mobile-logo {
        display: none;
        width: 28px;
        height: 28px;
        object-fit: contain;
        flex: none;
      }
      .topbar__toggle,
      .topbar__icon-btn {
        background: var(--app-surface);
        border: 1px solid var(--color-border);
        color: var(--text-secondary);
        width: 40px;
        height: 40px;
        border-radius: var(--radius-md);
        display: grid;
        place-items: center;
        cursor: pointer;
        transition: all var(--t-fast) var(--ease);
      }
      .topbar__toggle:hover,
      .topbar__icon-btn:hover {
        color: var(--navy-700);
        border-color: var(--navy-700);
        background: var(--navy-50);
      }
      .topbar__company {
        display: flex;
        align-items: center;
        gap: 8px;
        height: 40px;
        padding: 0 12px;
        border: 1px solid var(--color-border-input);
        border-radius: var(--radius-md);
        background: var(--app-surface);
        color: var(--text-secondary);
        transition: border var(--t-fast) var(--ease);
      }
      .topbar__company:focus-within {
        border-color: var(--navy-700);
        box-shadow: 0 0 0 3px rgba(46, 66, 123, 0.1);
      }
      .topbar__company--empty {
        opacity: 0.65;
      }
      .topbar__company--empty select {
        cursor: default;
      }
      .topbar__company select {
        border: none;
        background: transparent;
        color: var(--app-text);
        font-weight: 600;
        font-family: inherit;
        font-size: 13.5px;
        cursor: pointer;
        outline: none;
      }
      .topbar__user {
        display: flex;
        align-items: center;
        gap: 10px;
      }
      .topbar__avatar {
        width: 38px;
        height: 38px;
        border-radius: 50%;
        background: var(--navy-700);
        color: #ffffff;
        display: grid;
        place-items: center;
        font-weight: 700;
        font-size: 13px;
        letter-spacing: 0.03em;
      }
      .topbar__user-info {
        display: flex;
        flex-direction: column;
        line-height: 1.25;
      }
      .topbar__user-info strong {
        font-size: 13.5px;
        color: var(--app-text);
      }
      .topbar__user-info small {
        color: var(--text-muted);
        font-size: 11.5px;
      }
      .content {
        flex: 1;
        overflow-y: auto;
        padding: 24px 32px;
      }
      @media (max-width: 1023px) {
        .shell {
          grid-template-columns: 1fr;
        }
        .sidebar {
          position: fixed;
          inset: 0 auto 0 0;
          z-index: var(--z-drawer);
          width: min(320px, 86vw);
          transform: translateX(-100%);
          box-shadow: var(--shadow-xl);
        }
        .sidebar.sidebar--mobile-open {
          transform: translateX(0);
        }
        .mobile-nav-overlay {
          position: fixed;
          inset: 0;
          z-index: var(--z-dropdown);
          border: 0;
          background: rgba(11, 19, 50, 0.5);
          cursor: pointer;
        }
        .topbar {
          position: sticky;
          top: 0;
          min-height: 56px;
          padding: 0 16px;
        }
      }
      @media (max-width: 860px) {
        .content {
          padding: 16px;
        }
        .topbar__user-info {
          display: none;
        }
      }
      @media (max-width: 640px) {
        .topbar {
          padding: 0 12px;
          gap: 8px;
          min-height: 56px;
        }
        .topbar__mobile-logo {
          display: block;
        }
        .topbar__spacer {
          display: none;
        }
        .topbar__company {
          flex: 1 1 0;
          min-width: 0;
          max-width: none;
          padding: 0 6px;
          height: 36px;
        }
        .topbar__company app-icon {
          display: none;
        }
        .topbar__company select {
          width: 100%;
          min-width: 0;
          font-size: 12px;
        }
        .topbar__icon-btn:not(:last-child),
        .topbar__user {
          display: none;
        }
        .content {
          padding: 12px;
        }
      }
    `,
  ],
})
export class Shell implements OnInit {
  readonly session = inject(SessionStore);
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly pointeuseApi = inject(PointeuseDatabasesService);

  private readonly sageApi = inject(SageDatabasesService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly collapsed = signal(false);
  readonly mobileMenuOpen = signal(false);
  readonly isMobileViewport = signal(false);
  readonly collapsedSections = signal<ReadonlySet<string>>(new Set());
  readonly databases = computed(() =>
    this.session.databases().filter((db) => this.session.hasPermission(`SAGE:${db.nomBd}:READ`, Droit.Databases)),
  );
  readonly pointeuseDbs = computed(() =>
    this.session.pointeuseDatabases().filter((db) =>
      this.session.hasPermission(`POINTEUSE:${db.nomBd}:READ`, Droit.Databases),
    ),
  );

  readonly visibleSections = computed<NavSection[]>(() =>
    NAV_SECTIONS.map((section) => ({
      ...section,
      items: section.items.filter((item) => this.session.hasPermission(item.permission, item.droit)),
    })).filter((section) => section.items.length > 0),
  );

  readonly initials = computed(() => {
    const name = this.session.displayName().trim();
    if (!name) return 'ST';
    const parts = name.split(/\s+/);
    return (parts[0][0] + (parts[1]?.[0] ?? '')).toUpperCase();
  });

  ngOnInit(): void {
    this.syncViewport();
    const pending = sessionStorage.getItem('st_pending_toast');
    if (pending) {
      sessionStorage.removeItem('st_pending_toast');
      this.toast.success(pending);
    }
    this.auth.refreshMe().subscribe();
    this.pointeuseApi.refresh().subscribe();
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    this.syncViewport();
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    this.closeMobileNavigation();
  }

  toggleNavigation(): void {
    if (this.isMobileViewport()) {
      this.mobileMenuOpen.update((open) => !open);
      if (this.mobileMenuOpen()) this.collapsed.set(false);
      return;
    }
    this.collapsed.update((collapsed) => !collapsed);
  }

  closeMobileNavigation(): void {
    if (this.isMobileViewport()) this.mobileMenuOpen.set(false);
  }

  isSectionExpanded(title: string): boolean {
    return !this.collapsedSections().has(title);
  }

  toggleSection(title: string): void {
    this.collapsedSections.update((sections) => {
      const next = new Set(sections);
      if (next.has(title)) next.delete(title);
      else next.add(title);
      return next;
    });
  }

  onCompanyChange(event: Event): void {
    this.session.setActiveSageDb((event.target as HTMLSelectElement).value || null);
    this.activateThenReload();
  }

  onPointeuseChange(event: Event): void {
    this.session.setActivePointeuseDb((event.target as HTMLSelectElement).value || null);
    this.activateThenReload();
  }

  private activateThenReload(): void {
    if (!this.session.activeSageDb()) {
      window.location.reload();
      return;
    }
    this.sageApi.activate().subscribe({
      next: (r) => {
        if (r.cardsAdded > 0 || r.cardsDeactivated > 0) {
          sessionStorage.setItem('st_pending_toast', r.message);
        }
        window.location.reload();
      },
      error: () => window.location.reload(),
    });
  }

  async logout(): Promise<void> {
    const ok = await this.confirm.ask('Voulez-vous vraiment vous déconnecter ?', 'Déconnexion', 'Se déconnecter');
    if (!ok) return;
    this.auth.logout();
    this.router.navigate(['/login']);
  }

  private syncViewport(): void {
    const mobile = window.matchMedia('(max-width: 1023px)').matches;
    this.isMobileViewport.set(mobile);
    if (mobile) this.collapsed.set(false);
    else this.mobileMenuOpen.set(false);
  }
}
