import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

/** Un écart à afficher : libellé et valeur signée (positive = hausse). */
export interface DeltaPoint {
  label: string;
  value: number;
}

/**
 * Barres divergentes autour d'un axe central (écarts signés entre deux périodes).
 *
 * Même parti pris que `soft-bar-chart` : barres en CSS, libellés en texte HTML, couleurs par
 * jetons du thème. Le sens se lit par trois canaux à la fois — le côté de l'axe, la couleur
 * (paire chaude/froide validée pour le daltonisme) et le texte signé (« +3 de plus ») — jamais
 * par la couleur seule. Les valeurs restent à l'encre du texte, pas à la couleur de la barre.
 */
@Component({
  selector: 'soft-delta-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (rows().length) {
      <ul class="soft-delta" [attr.aria-label]="ariaLabel() || null">
        @for (row of rows(); track row.label) {
          <li class="soft-delta__row">
            <span class="soft-delta__label" [title]="row.label">{{ row.label }}</span>
            <span class="soft-delta__half soft-delta__half--down" aria-hidden="true">
              @if (row.value < 0) {
                <span class="soft-delta__fill soft-delta__fill--down" [style.width.%]="row.percent"></span>
              }
            </span>
            <span class="soft-delta__half soft-delta__half--up" aria-hidden="true">
              @if (row.value > 0) {
                <span class="soft-delta__fill soft-delta__fill--up" [style.width.%]="row.percent"></span>
              }
            </span>
            <span class="soft-delta__value">{{ row.text }}</span>
          </li>
        }
      </ul>
    } @else {
      <p class="soft-delta__empty">{{ emptyText() }}</p>
    }
  `,
  styles: [
    `
      .soft-delta {
        margin: 0;
        padding: 0;
        list-style: none;
        display: flex;
        flex-direction: column;
        gap: 10px;
      }
      .soft-delta__row {
        display: grid;
        grid-template-columns: minmax(90px, 30%) 1fr 1fr minmax(96px, auto);
        align-items: center;
        column-gap: 0;
        font-size: 13px;
      }
      .soft-delta__label {
        padding-right: 10px;
        color: var(--text-secondary);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .soft-delta__half {
        display: flex;
        height: 10px;
        background: var(--surface-dark);
      }
      /* L'axe central (zéro) est la bordure droite de la moitié « baisse ». */
      .soft-delta__half--down {
        justify-content: flex-end;
        border-radius: var(--radius-full) 0 0 var(--radius-full);
        border-right: 2px solid var(--text-muted);
      }
      .soft-delta__half--up {
        justify-content: flex-start;
        border-radius: 0 var(--radius-full) var(--radius-full) 0;
      }
      .soft-delta__fill {
        display: block;
        height: 100%;
        min-width: 2px;
        transition: width var(--t-normal) var(--ease);
      }
      .soft-delta__fill--down {
        background: var(--delta-down, var(--info));
        border-radius: var(--radius-full) 0 0 var(--radius-full);
      }
      .soft-delta__fill--up {
        background: var(--delta-up, var(--danger));
        border-radius: 0 var(--radius-full) var(--radius-full) 0;
      }
      .soft-delta__value {
        padding-left: 10px;
        font-variant-numeric: tabular-nums;
        font-weight: 600;
        color: var(--app-text);
        white-space: nowrap;
      }
      .soft-delta__empty {
        margin: 0;
        color: var(--text-muted);
        font-size: 13px;
      }
      @media (prefers-reduced-motion: reduce) {
        .soft-delta__fill { transition: none; }
      }
    `,
  ],
})
export class SoftDeltaChart {
  readonly items = input<DeltaPoint[]>([]);
  /** Mot accolé à une hausse et à une baisse (« +3 de plus », « −2 de moins »). */
  readonly upWord = input('de plus');
  readonly downWord = input('de moins');
  readonly sameWord = input('inchangé');
  readonly emptyText = input('Aucun écart à afficher.');
  readonly ariaLabel = input('');

  readonly rows = computed(() => {
    const items = this.items();
    const max = items.length ? Math.max(...items.map((i) => Math.abs(i.value))) : 0;
    return items.map((item) => ({
      ...item,
      percent: max > 0 ? (Math.abs(item.value) / max) * 100 : 0,
      text: this.text(item.value),
    }));
  });

  private text(value: number): string {
    if (value > 0) return `+${value} ${this.upWord()}`;
    if (value < 0) return `−${-value} ${this.downWord()}`;
    return this.sameWord();
  }
}
