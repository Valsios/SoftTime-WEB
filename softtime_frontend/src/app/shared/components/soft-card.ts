import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'soft-card',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="soft-card" [class]="classes()">
      <ng-content></ng-content>
    </div>
  `,
})
export class SoftCard {
  readonly variant = input<'default' | 'bordered' | 'elevated' | 'interactive'>('default');
  readonly padding = input<'sm' | 'md' | 'lg'>('md');

  classes(): string {
    const parts = [`soft-card--pad-${this.padding()}`];
    if (this.variant() === 'elevated') parts.push('soft-card--elevated');
    if (this.variant() === 'interactive') parts.push('soft-card--interactive');
    return parts.join(' ');
  }
}
