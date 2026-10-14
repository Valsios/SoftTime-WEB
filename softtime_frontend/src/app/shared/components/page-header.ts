import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'page-header',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="soft-page-header">
      <div>
        <div class="soft-eyebrow">
          <span class="soft-eyebrow-line"></span>
          <span>{{ eyebrow() || 'CONSOLE DE GESTION' }}</span>
        </div>
        <h1>{{ title() }}</h1>
        @if (subtitle()) {
          <p>{{ subtitle() }}</p>
        }
      </div>
      <div class="soft-actions">
        <ng-content></ng-content>
      </div>
    </div>
  `,
})
export class PageHeader {
  readonly title = input('');
  readonly subtitle = input('');
  readonly eyebrow = input('');
}
