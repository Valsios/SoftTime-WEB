import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/** Champ de recherche des droits : il transmet le texte saisi, le filtrage est fait par la page. */
@Component({
  selector: 'perm-search',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="ps" role="search">
      <label class="ps-label" for="swPermSearch">Rechercher un droit</label>
      <input
        id="swPermSearch"
        class="ps-input"
        type="search"
        autocomplete="off"
        placeholder="Ex. : pointages, utilisateurs"
        [value]="value()"
        (input)="changed.emit($any($event.target).value)"
      />
      @if (value()) {
        <button type="button" class="ps-clear" (click)="changed.emit('')">Effacer</button>
      }
    </div>
  `,
  styles: [
    `
      .ps { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; padding: 12px 16px; margin-bottom: 16px;
        background: var(--app-surface); border: 1px solid var(--app-border); border-radius: 8px; }
      .ps-label { font-weight: 600; color: var(--app-text-strong); }
      .ps-input { flex: 1 1 220px; min-width: 0; padding: 8px 10px; color: var(--app-text); background: var(--app-surface);
        border: 1px solid var(--app-border); border-radius: 6px; font: inherit; }
      .ps-clear { padding: 6px 12px; color: var(--app-text); background: var(--app-surface); border: 1px solid var(--app-border);
        border-radius: 6px; font: inherit; cursor: pointer; }
    `,
  ],
})
export class PermSearch {
  readonly value = input('');
  readonly changed = output<string>();
}
