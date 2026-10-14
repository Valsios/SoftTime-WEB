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
        gap: 6px;
        margin: 0 0 20px;
        border-bottom: 1px solid var(--color-border);
      }
      .soft-tabs__item {
        border: 0;
        background: transparent;
        padding: 10px 18px;
        cursor: pointer;
        font-family: var(--font-sans);
        font-size: 14px;
        font-weight: 600;
        color: var(--text-secondary);
        border-bottom: 2px solid transparent;
        margin-bottom: -1px;
        transition: all var(--t-fast) var(--ease);
      }
      .soft-tabs__item:hover {
        color: var(--navy-900);
      }
      .soft-tabs__item.is-active {
        color: var(--navy-700);
        border-bottom-color: var(--navy-700);
        font-weight: 700;
      }
    `,
  ],
})
export class SoftTabs {
  readonly tabs = input<TabItem[]>([]);
  readonly activeTab = model('0');
}
