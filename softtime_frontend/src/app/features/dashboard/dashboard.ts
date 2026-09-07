import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { SessionStore } from '../../core/session.store';
import { Droit } from '../../core/rights';
import { SoftCard } from '../../shared/components/soft-card';
import { SoftBadge } from '../../shared/components/soft-badge';
import { NAV_SECTIONS } from '../../layout/nav';

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
          <dt>Base SAGE active</dt><dd>{{ session.activeSageDb() || '—' }}</dd>
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
        margin-bottom: 28px;
      }
      h3 { margin: 0 0 12px; font-size: 16px; }
      .dash-dl {
        margin: 0;
        display: grid;
        grid-template-columns: auto 1fr;
        gap: 6px 16px;
        font-size: 14px;
      }
      .dash-dl dt { color: var(--text-muted); }
      .dash-dl dd { margin: 0; font-weight: 600; }
      .dash-badges { display: flex; flex-wrap: wrap; gap: 8px; }
      .dash-section-title { font-size: 18px; margin: 0 0 14px; }
      .dash-links {
        display: grid;
        grid-template-columns: repeat(auto-fill, minmax(200px, 1fr));
        gap: 12px;
      }
      .dash-link {
        display: flex;
        flex-direction: column;
        gap: 4px;
        text-decoration: none;
        color: inherit;
      }
      .dash-link small { color: var(--text-muted); font-size: 12px; }
    `,
  ],
})
export class Dashboard {
  readonly session = inject(SessionStore);

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
