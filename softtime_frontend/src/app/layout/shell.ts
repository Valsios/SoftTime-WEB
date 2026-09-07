import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { SessionStore } from '../core/session.store';
import { AuthService } from '../shared/services/auth.service';
import { PointeuseDatabasesService } from '../shared/services/catalog.service';
import { NAV_SECTIONS, NavSection } from './nav';
import { Icon } from '../shared/components/icon';
import { PointeuseDb } from '../shared/models';

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Icon],
  template: `
    <div class="shell" [class.shell--mini]="collapsed()">
      <aside class="sidebar softtime-sidebar">
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
          >
            <app-icon name="home"></app-icon>
            @if (!collapsed()) {<span>Tableau de bord</span>}
          </a>

          @for (section of visibleSections(); track section.title) {
            <div class="sidebar__section">
              @if (!collapsed()) {<span class="sidebar__section-title">{{ section.title }}</span>}
              @for (item of section.items; track item.path) {
                @if (session.hasRight(item.droit)) {
                  <a
                    [routerLink]="item.path"
                    routerLinkActive="is-active"
                    class="sidebar__item"
                    [title]="item.label"
                  >
                    <app-icon [name]="item.icon"></app-icon>
                    @if (!collapsed()) {<span>{{ item.label }}</span>}
                  </a>
                }
              }
            </div>
          }
        </nav>
      </aside>

      <div class="main">
        <header class="topbar">
          <button class="topbar__toggle" type="button" (click)="collapsed.set(!collapsed())">
            <app-icon name="grid" [size]="18"></app-icon>
          </button>

          <div class="topbar__spacer"></div>

          @if (databases().length) {
            <div class="topbar__company">
              <app-icon name="server" [size]="16"></app-icon>
              <select
                [value]="session.activeSageDb() ?? ''"
                (change)="onCompanyChange($event)"
                aria-label="Société / base SAGE"
              >
                @for (db of databases(); track db.id) {
                  <option [value]="db.nomBd">{{ db.nomBd }}</option>
                }
              </select>
            </div>
          }

          @if (pointeuseDbs().length) {
            <div class="topbar__company">
              <select
                [value]="session.activePointeuseDb() ?? ''"
                (change)="onPointeuseChange($event)"
                aria-label="Base pointeuse"
              >
                @for (db of pointeuseDbs(); track db.id) {
                  <option [value]="db.nomBd">{{ db.nomBd }}</option>
                }
              </select>
            </div>
          }

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
        --sidebar-active-color: #4a90d9;
        --sidebar-active-bg: rgba(74, 144, 217, 0.15);
        color: var(--sidebar-text);
        display: flex;
        flex-direction: column;
        overflow-y: auto;
        overflow-x: hidden;
      }
      .sidebar__brand {
        display: flex;
        align-items: center;
        gap: 10px;
        padding: 18px 16px;
        border-bottom: 1px solid rgba(255, 255, 255, 0.06);
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
        color: #fff;
        font-size: 15px;
      }
      .sidebar__brand-text small {
        color: var(--sidebar-text-muted);
        font-size: 11px;
      }
      .sidebar__nav {
        padding: 8px;
        display: flex;
        flex-direction: column;
        gap: 2px;
      }
      .sidebar__section {
        margin-top: 12px;
        display: flex;
        flex-direction: column;
        gap: 2px;
      }
      .sidebar__section-title {
        font-size: 10px;
        text-transform: uppercase;
        letter-spacing: 0.08em;
        color: var(--sidebar-text-muted);
        padding: 6px 10px 2px;
      }
      .sidebar__item {
        display: flex;
        align-items: center;
        gap: 10px;
        padding: 9px 10px;
        border-radius: 8px;
        color: var(--sidebar-text);
        font-size: 13.5px;
        font-weight: 500;
        white-space: nowrap;
        transition: background var(--t-fast) var(--ease), color var(--t-fast) var(--ease);
      }
      .sidebar__item:hover {
        background: var(--sidebar-hover);
        color: #fff;
      }
      .sidebar__item.is-active {
        background: var(--sidebar-active-bg);
        color: var(--sidebar-active-color);
      }
      .main {
        display: flex;
        flex-direction: column;
        overflow: hidden;
      }
      .topbar {
        height: 78px;
        display: flex;
        align-items: center;
        gap: 14px;
        padding: 0 22px;
        background: rgba(255, 255, 255, 0.94);
        backdrop-filter: blur(12px);
        border-bottom: 1px solid var(--app-border);
        box-shadow: 0 10px 30px rgba(31, 41, 90, 0.06);
        z-index: var(--z-sticky);
      }
      :root[data-theme='dark'] .topbar {
        background: rgba(18, 24, 43, 0.9);
      }
      .topbar__spacer {
        flex: 1;
      }
      .topbar__toggle,
      .topbar__icon-btn {
        background: transparent;
        border: 1px solid var(--app-border);
        color: var(--text-secondary);
        width: 38px;
        height: 38px;
        border-radius: 10px;
        display: grid;
        place-items: center;
        cursor: pointer;
        transition: all var(--t-fast) var(--ease);
      }
      .topbar__toggle:hover,
      .topbar__icon-btn:hover {
        color: var(--softtime);
        border-color: var(--primary-200);
      }
      .topbar__company {
        display: flex;
        align-items: center;
        gap: 8px;
        padding: 6px 10px;
        border: 1px solid var(--app-border);
        border-radius: 10px;
        color: var(--text-secondary);
      }
      .topbar__company select {
        border: none;
        background: transparent;
        color: var(--app-text);
        font-weight: 600;
        font-family: inherit;
        font-size: 14px;
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
        background: var(--softtime);
        color: #fff;
        display: grid;
        place-items: center;
        font-weight: 700;
        font-size: 14px;
      }
      .topbar__user-info {
        display: flex;
        flex-direction: column;
        line-height: 1.2;
      }
      .topbar__user-info strong {
        font-size: 14px;
      }
      .topbar__user-info small {
        color: var(--text-muted);
        font-size: 12px;
      }
      .content {
        flex: 1;
        overflow-y: auto;
        padding: 24px;
      }
      @media (max-width: 860px) {
        .topbar__user-info {
          display: none;
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

  readonly collapsed = signal(false);
  readonly databases = computed(() => this.session.databases());
  readonly pointeuseDbs = signal<PointeuseDb[]>([]);

  readonly visibleSections = computed<NavSection[]>(() =>
    NAV_SECTIONS.filter(
      (s) => this.session.hasRight(s.droit) && s.items.some((i) => this.session.hasRight(i.droit)),
    ),
  );

  readonly initials = computed(() => {
    const name = this.session.displayName().trim();
    if (!name) return 'ST';
    const parts = name.split(/\s+/);
    return (parts[0][0] + (parts[1]?.[0] ?? '')).toUpperCase();
  });

  ngOnInit(): void {
    this.pointeuseApi.list().subscribe({
      next: (list) => {
        this.pointeuseDbs.set(list);
        const names = list.map((d) => d.nomBd).filter((n): n is string => !!n);
        const current = this.session.activePointeuseDb();
        if (!current || !names.includes(current)) {
          const preferred = list.find((d) => d.active)?.nomBd ?? names[0] ?? null;
          this.session.setActivePointeuseDb(preferred);
        }
      },
    });
  }

  onCompanyChange(event: Event): void {
    this.session.setActiveSageDb((event.target as HTMLSelectElement).value || null);
    window.location.reload();
  }

  onPointeuseChange(event: Event): void {
    this.session.setActivePointeuseDb((event.target as HTMLSelectElement).value || null);
    window.location.reload();
  }

  logout(): void {
    this.auth.logout();
    this.router.navigate(['/login']);
  }
}
