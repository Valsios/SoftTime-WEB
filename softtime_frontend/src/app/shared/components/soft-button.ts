import { ChangeDetectionStrategy, Component, input } from '@angular/core';

type Variant = 'primary' | 'secondary' | 'accent' | 'danger' | 'ghost';
type Size = 'sm' | 'md' | 'lg';

@Component({
  selector: 'soft-button',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <button
      [type]="type()"
      class="soft-btn"
      [class]="classes()"
      [disabled]="disabled() || loading()"
    >
      @if (loading()) {
        <span class="soft-btn__spinner"></span>
      }
      <ng-content></ng-content>
    </button>
  `,
})
export class SoftButton {
  readonly variant = input<Variant>('primary');
  readonly size = input<Size>('md');
  readonly disabled = input(false);
  readonly loading = input(false);
  readonly fullWidth = input(false);
  readonly type = input<'button' | 'submit'>('button');

  classes(): string {
    const parts = [`soft-btn--${this.variant()}`];
    if (this.size() !== 'md') parts.push(`soft-btn--${this.size()}`);
    if (this.fullWidth()) parts.push('soft-btn--full');
    return parts.join(' ');
  }
}
