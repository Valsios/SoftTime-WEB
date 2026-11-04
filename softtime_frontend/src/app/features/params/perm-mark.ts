import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/** Signe « + » (ajouté) ou « − » (retiré) par rapport au rôle, lisible sans la couleur et avec un texte accessible. */
@Component({
  selector: 'perm-mark',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (kind() === 'added') {
      <span class="pm pm--added" role="img" title="Ajouté pour ce compte" aria-label="Ajouté pour ce compte">+</span>
    } @else if (kind() === 'removed') {
      <span class="pm pm--removed" role="img" title="Retiré pour ce compte" aria-label="Retiré pour ce compte">&minus;</span>
    }
  `,
  styles: [
    `
      .pm { display: inline-flex; align-items: center; justify-content: center; min-width: 16px; height: 16px; margin-left: 6px;
        border: 1px solid currentColor; font-size: 12px; font-weight: 700; line-height: 1; }
      .pm--added { color: var(--success); }
      .pm--removed { color: var(--danger); }
    `,
  ],
})
export class PermMark {
  readonly kind = input<'added' | 'removed' | null>(null);
}
