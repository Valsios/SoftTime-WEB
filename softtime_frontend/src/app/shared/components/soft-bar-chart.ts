import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ChartFormatter, ChartPoint } from './chart.util';

/**
 * Barres horizontales proportionnelles (répartition d'un effectif, d'un volume…).
 *
 * Barres en CSS plutôt qu'en SVG : le libellé reste du texte HTML — donc sélectionnable,
 * tronquable par `text-overflow` et accessible — et les couleurs suivent le thème par
 * variables CSS.
 */
@Component({
  selector: 'soft-bar-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (bars().length) {
      <ul class="soft-bars">
        @for (bar of bars(); track bar.label) {
          <li class="soft-bars__row">
            <span class="soft-bars__label" [title]="bar.label">{{ bar.label }}</span>
            <span class="soft-bars__track">
              <span
                class="soft-bars__fill"
                [style.width.%]="bar.percent"
                [style.background]="bar.color"
              ></span>
            </span>
            <span class="soft-bars__value">{{ format()(bar.value) }}</span>
          </li>
        }
      </ul>
    } @else {
      <p class="soft-bars__empty">{{ emptyText() }}</p>
    }
  `,
  styles: [
    `
      .soft-bars {
        margin: 0;
        padding: 0;
        list-style: none;
        display: flex;
        flex-direction: column;
        gap: 10px;
      }
      .soft-bars__row {
        display: grid;
        grid-template-columns: minmax(90px, 34%) 1fr auto;
        align-items: center;
        gap: 12px;
        font-size: 13px;
      }
      .soft-bars__label {
        color: var(--text-secondary);
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .soft-bars__track {
        display: block;
        height: 10px;
        border-radius: var(--radius-full);
        background: var(--surface-dark);
        overflow: hidden;
      }
      .soft-bars__fill {
        display: block;
        height: 100%;
        border-radius: var(--radius-full);
        min-width: 2px;
        transition: width var(--t-normal) var(--ease);
      }
      .soft-bars__value {
        font-variant-numeric: tabular-nums;
        font-weight: 600;
        color: var(--app-text);
      }
      .soft-bars__empty {
        margin: 0;
        color: var(--text-muted);
        font-size: 13px;
      }
    `,
  ],
})
export class SoftBarChart {
  readonly items = input<ChartPoint[]>([]);
  readonly color = input('var(--primary-500)');
  readonly emptyText = input('Aucune donnée sur la période.');
  readonly format = input<ChartFormatter>((value) => String(value ?? 0));

  readonly bars = computed(() => {
    const items = this.items();
    const max = items.length ? Math.max(...items.map((item) => item.value)) : 0;
    return items.map((item) => ({
      ...item,
      color: this.color(),
      percent: max > 0 ? (item.value / max) * 100 : 0,
    }));
  });
}
