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
          [attr.aria-selected]="activeTab() === tab.id"
          [attr.tabindex]="activeTab() === tab.id ? 0 : -1"
          (click)="activeTab.set(tab.id)"
          (keydown)="onKeydown($event, $index)"
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
        color: var(--accent);
        border-bottom-color: var(--accent);
        font-weight: 700;
      }
    `,
  ],
})
export class SoftTabs {
  readonly tabs = input<TabItem[]>([]);
  readonly activeTab = model('0');

  onKeydown(event: KeyboardEvent, index: number): void {
    const tabs = this.tabs();
    if (!tabs.length) return;
    let next = index;
    if (event.key === 'ArrowRight' || event.key === 'ArrowDown') next = (index + 1) % tabs.length;
    else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') next = (index - 1 + tabs.length) % tabs.length;
    else if (event.key === 'Home') next = 0;
    else if (event.key === 'End') next = tabs.length - 1;
    else return;

    event.preventDefault();
    this.activeTab.set(tabs[next].id);
    const tablist = (event.currentTarget as HTMLElement).parentElement;
    tablist?.querySelectorAll<HTMLButtonElement>('[role="tab"]')[next]?.focus();
  }
}
