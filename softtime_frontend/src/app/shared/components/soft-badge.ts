import { ChangeDetectionStrategy, Component, input } from '@angular/core';

type BadgeVariant =
  | 'primary'
  | 'success'
  | 'warning'
  | 'danger'
  | 'info'
  | 'orange'
  | 'muted';

@Component({
  selector: 'soft-badge',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <span class="soft-badge" [class]="'soft-badge--' + variant()">
      <ng-content></ng-content>
    </span>
  `,
})
export class SoftBadge {
  readonly variant = input<BadgeVariant>('primary');
}
