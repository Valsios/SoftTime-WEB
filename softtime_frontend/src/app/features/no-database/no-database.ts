import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { NO_DATABASE_MESSAGE } from '../../core/access.logic';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';

/** État vide partagé : aucune base SAGE n'est utilisable pour le compte (un seul mécanisme, pas une copie par page). */
@Component({
  selector: 'app-no-database',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink],
  template: `
    <section class="no-db" role="status">
      <h2 class="no-db__title">Aucune base disponible</h2>
      <p class="no-db__text">{{ message }}</p>
      @if (canConfigure()) {
        <a class="no-db__link" routerLink="/sage-databases">Configurer une base</a>
      }
    </section>
  `,
  styles: [
    `
      .no-db { max-width: 560px; margin: 64px auto; padding: 28px 24px; text-align: center;
        border: 1px solid var(--color-border); background: var(--surface-dark); color: var(--app-text); }
      .no-db__title { margin: 0 0 8px; font-family: var(--font-heading); font-size: 18px; font-weight: 600; color: var(--navy-900); }
      .no-db__text { margin: 0; font-size: 14px; line-height: 1.5; color: var(--text-secondary); }
      .no-db__link { display: inline-block; margin-top: 16px; font-weight: 600; color: var(--navy-700); }
    `,
  ],
})
export class NoDatabasePage {
  private readonly session = inject(SessionStore);
  readonly message = NO_DATABASE_MESSAGE;
  canConfigure(): boolean {
    return this.session.hasPermission('SAGE', Droit.Databases);
  }
}
