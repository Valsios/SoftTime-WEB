import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionStore } from '../../core/session.store';
import { Droit } from '../../core/rights';
import { SoftCard } from '../../shared/components/soft-card';
import { SoftBadge } from '../../shared/components/soft-badge';
import { NAV_SECTIONS } from '../../layout/nav';
import { DestroyRef, signal } from '@angular/core';
import { ClosingService } from '../../shared/services/closing.service';
import { dashboardCardView, defaultPeriod } from '../closing/closing.logic';

const DROIT_LABELS: Record<number, string> = {
  [Droit.Databases]: 'Bases de données',
  [Droit.Parameters]: 'Paramètres',
  [Droit.Processing]: 'Traitement',
  [Droit.Other]: 'Divers',
  [Droit.Traceability]: 'Traçabilité',
  [Droit.Reports]: 'États',
};

@Component({
  selector: 'app-dashboard',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, SoftCard, SoftBadge],
  template: `
    <div class="soft-page-header">
      <div>
        <h1>Tableau de bord</h1>
        <p>Bienvenue, {{ session.displayName() }}</p>
      </div>
    </div>

    <div class="dash-grid">
      <soft-card>
        <h3>Session</h3>
        <dl class="dash-dl">
          <dt>Utilisateur</dt><dd>{{ session.displayName() }}</dd>
          <dt>Login</dt><dd>{{ session.session()?.login }}</dd>
          <dt>Base RH / paie active</dt><dd>{{ session.activeSageDb() || '—' }}</dd>
        </dl>
      </soft-card>

      <soft-card>
        <h3>Droits</h3>
        <div class="dash-badges">
          @for (r of session.rights(); track r) {
            <soft-badge variant="primary">{{ droitLabel(r) }}</soft-badge>
          } @empty {
            <span class="muted">Aucun droit assigné</span>
          }
        </div>
      </soft-card>
    </div>

    @if (closingCard(); as c) {
      <h2 class="dash-section-title">Contrôle avant paie</h2>
      <div class="dash-links">
        <soft-card>
          <div class="dash-closing">
            <span class="dash-closing__label">Mois précédent</span>
            <soft-badge [variant]="c.status.tone">{{ c.status.label }}</soft-badge>
            <strong>{{ c.score }}</strong>
            @if (c.critiques) {
              <small>{{ c.critiques }}</small>
            }
            <a routerLink="/closing" class="dash-closing__link">Voir le détail</a>
          </div>
        </soft-card>
      </div>
    }

    <h2 class="dash-section-title">Analyse</h2>
    <div class="dash-links">
      <a routerLink="/analytics" class="dash-link soft-card soft-card--interactive soft-card--pad-md">
        <strong>Analyse de la période</strong>
        <small>Absences, retards et indicateurs clés</small>
      </a>
    </div>

    <h2 class="dash-section-title">Accès rapide</h2>
    <div class="dash-links">
      @for (item of quickLinks(); track item.path) {
        <a [routerLink]="item.path" class="dash-link soft-card soft-card--interactive soft-card--pad-md">
          <strong>{{ item.label }}</strong>
          <small>{{ item.path }}</small>
        </a>
      }
    </div>
  `,
  styles: [
    `
      .dash-grid {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
        gap: 16px;
        margin-bottom: 32px;
      }
      h3 {
        margin: 0 0 14px;
        font-family: var(--font-heading);
        font-size: 16px;
        font-weight: 600;
        color: var(--navy-900);
      }
      .dash-dl {
        margin: 0;
        display: grid;
        grid-template-columns: auto 1fr;
        gap: 8px 16px;
        font-size: 14px;
      }
      .dash-dl dt { color: var(--text-muted); font-size: 13px; }
      .dash-dl dd { margin: 0; font-weight: 600; color: var(--app-text); }
      .dash-badges { display: flex; flex-wrap: wrap; gap: 8px; }
      .dash-section-title {
        font-family: var(--font-heading);
        font-size: 18px;
        font-weight: 600;
        color: var(--navy-950);
        margin: 0 0 16px;
      }
      .dash-links {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(210px, 1fr));
        gap: 14px;
        margin-bottom: 28px;
      }
      .dash-link {
        display: flex;
        flex-direction: column;
        gap: 4px;
        text-decoration: none;
        color: inherit;
        border-left: 3px solid transparent;
        transition: all var(--t-fast) var(--ease);
      }
      .dash-link:hover {
        border-left-color: var(--gold-400);
      }
      .dash-link strong {
        font-size: 14px;
        color: var(--navy-950);
      }
      .dash-link small { color: var(--text-muted); font-size: 12px; }
      .dash-closing { display: flex; flex-direction: column; align-items: flex-start; gap: 6px; }
      .dash-closing__label { color: var(--text-muted); font-size: 13px; }
      .dash-closing strong { font-size: 18px; color: var(--navy-950); }
      .dash-closing small { color: var(--text-secondary); font-size: 12px; }
      .dash-closing__link { color: var(--accent); font-weight: 600; font-size: 13px; }
      .dash-closing__link:focus-visible { outline: 2px solid var(--focus-ring); outline-offset: 2px; }
    `,
  ],
})
export class Dashboard {
  readonly session = inject(SessionStore);
  private readonly closing = inject(ClosingService);
  private readonly closingDestroy = inject(DestroyRef);

  /** Carte « Contrôle avant paie » : absente sans le droit, pendant le chargement et en cas d'échec (silencieux). */
  readonly closingCard = signal<ReturnType<typeof dashboardCardView> | null>(null);

  constructor() {
    // Même règle que le menu (closing.read + porte 6) et une base active : sinon, aucun appel.
    if (!this.session.hasPermission('closing.read', Droit.Reports) || !this.session.activeSageDb()) return;
    const sub = this.closing.analysisSilently({ ...defaultPeriod(), pageSize: 1 }).subscribe({
      next: (r) => this.closingCard.set(dashboardCardView(r)),
      error: () => this.closingCard.set(null),
    });
    this.closingDestroy.onDestroy(() => sub.unsubscribe());
  }

  readonly quickLinks = computed(() => {
    const links: { label: string; path: string }[] = [];
    for (const section of NAV_SECTIONS) {
      if (!this.session.hasRight(section.droit)) continue;
      for (const item of section.items) {
        if (this.session.hasRight(item.droit)) {
          links.push({ label: item.label, path: item.path });
        }
      }
    }
    return links.slice(0, 12);
  });

  droitLabel(id: number): string {
    return DROIT_LABELS[id] ?? `Droit ${id}`;
  }
}
