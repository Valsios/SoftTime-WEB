import { ChangeDetectionStrategy, Component, input, model } from '@angular/core';

export interface TabItem {
  id: string;
  label: string;
}

@Component({
  selector: 'soft-tabs',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="soft-tabs" role="tablist">
      @for (tab of tabs(); track tab.id) {
        <button
          type="button"
          role="tab"
          class="soft-tabs__item"
          [class.is-active]="activeTab() === tab.id"
          (click)="activeTab.set(tab.id)"
        >
          {{ tab.label }}
        </button>
      }
    </div>
  `,
  styles: [
    `
      .soft-tabs {
        display: flex;
        gap: 4px;
        margin: 0 0 16px;
        border-bottom: 1px solid var(--color-border, #e5e7eb);
      }
      .soft-tabs__item {
        border: 0;
        background: transparent;
        padding: 10px 16px;
        cursor: pointer;
        font-weight: 600;
        color: var(--text-secondary, #6b7280);
        border-bottom: 2px solid transparent;
        margin-bottom: -1px;
      }
      .soft-tabs__item.is-active {
        color: var(--primary-600, #1d4ed8);
        border-bottom-color: var(--primary-600, #1d4ed8);
      }
    `,
  ],
})
export class SoftTabs {
  readonly tabs = input<TabItem[]>([]);
  readonly activeTab = model('0');
}
