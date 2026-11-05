import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ChartFormatter, ChartPoint, colorAt } from './chart.util';

/**
 * Anneau (camembert) de répartition — `conic-gradient` CSS, sans dépendance.
 *
 * La légende porte le détail (valeur + pourcentage) : un camembert seul est illisible
 * dès qu'un secteur devient très petit.
 */
@Component({
  selector: 'soft-donut-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (slices().length) {
      <div class="soft-donut">
        <div class="soft-donut__ring" [style.background]="gradient()">
          <span class="soft-donut__hole">
            <strong>{{ format()(total()) }}</strong>
            <small>total</small>
          </span>
        </div>
        <ul class="soft-donut__legend">
          @for (slice of slices(); track slice.label) {
            <li>
              <span class="soft-donut__chip" [style.background]="slice.color"></span>
              <span class="soft-donut__name" [title]="slice.label">{{ slice.label }}</span>
              <strong class="soft-donut__value">{{ format()(slice.value) }}</strong>
              <small class="soft-donut__pct">{{ pct(slice.percent) }}</small>
            </li>
          }
        </ul>
      </div>
    } @else {
      <p class="soft-donut__empty">{{ emptyText() }}</p>
    }
  `,
  styles: [
    `
      .soft-donut {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: 22px;
      }
      .soft-donut__ring {
        width: 148px;
        height: 148px;
        border-radius: var(--radius-full);
        display: flex;
        align-items: center;
        justify-content: center;
        flex: none;
        background: var(--surface-dark);
      }
      .soft-donut__hole {
        width: 96px;
        height: 96px;
        border-radius: var(--radius-full);
        background: var(--app-surface);
        display: flex;
        flex-direction: column;
        align-items: center;
        justify-content: center;
        line-height: 1.15;
      }
      .soft-donut__hole strong {
        font-family: var(--font-heading);
        font-size: 22px;
        font-weight: 700;
        color: var(--navy-950);
        font-variant-numeric: tabular-nums;
      }
      .soft-donut__hole small {
        color: var(--text-muted);
        font-size: 11px;
        text-transform: uppercase;
        letter-spacing: 0.06em;
        font-weight: 600;
      }
      .soft-donut__legend {
        margin: 0;
        padding: 0;
        list-style: none;
        display: flex;
        flex-direction: column;
        gap: 8px;
        font-size: 13px;
        min-width: 190px;
        flex: 1;
      }
      .soft-donut__legend li {
        display: grid;
        grid-template-columns: 10px 1fr auto auto;
        align-items: center;
        gap: 8px;
      }
      .soft-donut__chip {
        width: 10px;
        height: 10px;
        border-radius: var(--radius-full);
      }
      .soft-donut__name {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        color: var(--text-secondary);
      }
      .soft-donut__value {
        font-variant-numeric: tabular-nums;
      }
      .soft-donut__pct {
        color: var(--text-muted);
        font-variant-numeric: tabular-nums;
        min-width: 44px;
        text-align: right;
      }
      .soft-donut__empty {
        margin: 0;
        color: var(--text-muted);
        font-size: 13px;
      }
    `,
  ],
})
export class SoftDonutChart {
  readonly items = input<ChartPoint[]>([]);
  readonly emptyText = input('Aucune donnée sur la période.');
  readonly format = input<ChartFormatter>((value) => String(value ?? 0));

  readonly total = computed(() => this.items().reduce((sum, item) => sum + item.value, 0));

  readonly slices = computed(() => {
    const total = this.total();
    let offset = 0;
    return this.items().map((item, index) => {
      const percent = total > 0 ? (item.value / total) * 100 : 0;
      const slice = {
        label: item.label,
        value: item.value,
        percent,
        color: colorAt(index),
        start: offset,
        end: offset + percent,
      };
      offset += percent;
      return slice;
    });
  });

  readonly gradient = computed(() => {
    const slices = this.slices();
    if (!slices.length) return 'var(--surface-dark)';
    return `conic-gradient(${slices.map((s) => `${s.color} ${s.start}% ${s.end}%`).join(', ')})`;
  });

  pct(value: number): string {
    return `${value.toFixed(1)} %`;
  }
}
