import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ChartFormatter, ChartSeries } from './chart.util';

/**
 * Courbe(s) d'évolution — SVG pur, sans dépendance.
 *
 * Le graphique partage un axe X commun à toutes les séries : c'est pour cela que le
 * back-end renvoie systématiquement une ligne par période de la plage demandée (0 si
 * aucun événement), sinon les séries auraient des longueurs différentes.
 */
@Component({
  selector: 'soft-line-chart',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (hasData()) {
      <svg class="soft-chart__svg" [attr.viewBox]="'0 0 ' + width + ' ' + height()" role="img">
        @for (line of grid(); track line.y) {
          <line
            class="soft-chart__grid"
            [attr.x1]="pad.left"
            [attr.x2]="width - pad.right"
            [attr.y1]="line.y"
            [attr.y2]="line.y"
          />
          <text class="soft-chart__tick" [attr.x]="pad.left - 8" [attr.y]="line.y + 4" text-anchor="end">
            {{ line.label }}
          </text>
        }

        @for (serie of series(); track serie.name) {
          <polyline
            class="soft-chart__line"
            [attr.points]="points(serie)"
            [attr.stroke]="serie.color"
          />
          @for (dot of dots(serie); track $index) {
            <circle class="soft-chart__dot" [attr.cx]="dot.x" [attr.cy]="dot.y" r="3.5" [attr.fill]="serie.color">
              <title>{{ serie.name }} · {{ dot.label }} : {{ format()(dot.value) }}</title>
            </circle>
          }
        }

        @for (tick of xTicks(); track tick.x) {
          <text class="soft-chart__tick" [attr.x]="tick.x" [attr.y]="height() - 8" text-anchor="middle">
            {{ tick.label }}
          </text>
        }
      </svg>

      <ul class="soft-chart__legend">
        @for (serie of series(); track serie.name) {
          <li>
            <span class="soft-chart__chip" [style.background]="serie.color"></span>{{ serie.name }}
          </li>
        }
      </ul>
    } @else {
      <p class="soft-chart__empty">{{ emptyText() }}</p>
    }
  `,
  styles: [
    `
      .soft-chart__svg {
        display: block;
        width: 100%;
        height: auto;
        overflow: visible;
      }
      .soft-chart__grid {
        stroke: var(--border-light);
        stroke-width: 1;
        vector-effect: non-scaling-stroke;
      }
      .soft-chart__tick {
        fill: var(--text-muted);
        font-size: 11px;
        font-family: var(--font-sans);
      }
      .soft-chart__line {
        fill: none;
        stroke-width: 2;
        stroke-linejoin: round;
        stroke-linecap: round;
        vector-effect: non-scaling-stroke;
      }
      .soft-chart__legend {
        display: flex;
        flex-wrap: wrap;
        gap: 8px 18px;
        margin: 10px 0 0;
        padding: 0;
        list-style: none;
        font-size: 12px;
        color: var(--text-secondary);
      }
      .soft-chart__legend li {
        display: flex;
        align-items: center;
        gap: 6px;
      }
      .soft-chart__chip {
        width: 10px;
        height: 10px;
        border-radius: var(--radius-full);
        flex: none;
      }
      .soft-chart__empty {
        margin: 0;
        color: var(--text-muted);
        font-size: 13px;
      }
    `,
  ],
})
export class SoftLineChart {
  readonly labels = input<string[]>([]);
  readonly series = input<ChartSeries[]>([]);
  readonly height = input(250);
  readonly emptyText = input('Aucune donnée sur la période.');
  readonly format = input<ChartFormatter>((value) => String(value ?? 0));

  /** Largeur du viewBox : le SVG est ensuite mis à l'échelle par le CSS. */
  readonly width = 640;
  readonly pad = { left: 54, right: 16, top: 14, bottom: 30 };

  readonly hasData = computed(
    () => this.labels().length > 0 && this.series().some((serie) => serie.values.length > 0),
  );

  private readonly max = computed(() => {
    const values = this.series().flatMap((serie) => serie.values);
    const max = values.length ? Math.max(...values) : 0;
    return max > 0 ? max : 1;
  });

  readonly grid = computed(() => {
    const max = this.max();
    return [1, 0.75, 0.5, 0.25, 0].map((ratio) => ({
      y: this.y(max * ratio),
      label: String(Math.round(max * ratio * 10) / 10),
    }));
  });

  readonly xTicks = computed(() => {
    const count = this.labels().length;
    if (count === 0) return [];
    // Au-delà de 8 points, un libellé sur k seulement : sinon l'axe devient illisible.
    const step = Math.ceil(count / 8);
    const ticks: { x: number; label: string }[] = [];
    for (let i = 0; i < count; i += step) ticks.push({ x: this.x(i), label: this.labels()[i] });
    return ticks;
  });

  points(serie: ChartSeries): string {
    return serie.values
      .map((value, index) => `${this.x(index).toFixed(1)},${this.y(value).toFixed(1)}`)
      .join(' ');
  }

  dots(serie: ChartSeries): { x: number; y: number; value: number; label: string }[] {
    return serie.values.map((value, index) => ({
      x: this.x(index),
      y: this.y(value),
      value,
      label: this.labels()[index] ?? '',
    }));
  }

  private x(index: number): number {
    const usable = this.width - this.pad.left - this.pad.right;
    const count = this.labels().length;
    return count <= 1 ? this.pad.left + usable / 2 : this.pad.left + (usable * index) / (count - 1);
  }

  private y(value: number): number {
    const usable = this.height() - this.pad.top - this.pad.bottom;
    return this.pad.top + usable * (1 - value / this.max());
  }
}
