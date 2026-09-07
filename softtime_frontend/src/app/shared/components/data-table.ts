import { ChangeDetectionStrategy, Component, contentChild, input, model, TemplateRef } from '@angular/core';
import { CommonModule } from '@angular/common';

export interface Column {
  key: string;
  label: string;
  align?: 'left' | 'right' | 'center';
  /** Optional value formatter. Receives the raw cell value and full row. */
  format?: (value: unknown, row: Record<string, unknown>) => string;
}

@Component({
  selector: 'data-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule],
  template: `
    @if (loading()) {
      <div class="soft-spinner-lg"></div>
    } @else {
      <div class="soft-table-wrap">
        <table class="soft-table">
          <thead>
            <tr>
              @if (selectable()) {
                <th class="soft-table__check">
                  <input
                    type="checkbox"
                    [checked]="allSelected()"
                    [indeterminate]="someSelected()"
                    (change)="toggleAll($event)"
                    aria-label="Tout sélectionner"
                  />
                </th>
              }
              @for (col of columns(); track col.key) {
                <th [style.text-align]="col.align || 'left'">{{ col.label }}</th>
              }
              @if (actionsTpl()) {
                <th style="text-align:right">Actions</th>
              }
            </tr>
          </thead>
          <tbody>
            @for (row of rows(); track trackId(row, $index)) {
              <tr [class.is-selected]="selectable() && isSelected(row)">
                @if (selectable()) {
                  <td class="soft-table__check">
                    <input
                      type="checkbox"
                      [checked]="isSelected(row)"
                      (change)="toggleRow(row)"
                      [attr.aria-label]="'Sélectionner ' + rowKey(row)"
                    />
                  </td>
                }
                @for (col of columns(); track col.key) {
                  <td [style.text-align]="col.align || 'left'">
                    {{ display(col, row) }}
                  </td>
                }
                @if (actionsTpl(); as tpl) {
                  <td style="text-align:right">
                    <ng-container
                      [ngTemplateOutlet]="tpl"
                      [ngTemplateOutletContext]="{ $implicit: row }"
                    ></ng-container>
                  </td>
                }
              </tr>
            } @empty {
              <tr>
                <td class="soft-table__empty" [attr.colspan]="colspan()">
                  {{ emptyText() }}
                </td>
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class DataTable {
  readonly columns = input<Column[]>([]);
  readonly rows = input<Record<string, unknown>[]>([]);
  readonly loading = input(false);
  readonly trackKey = input('id');
  readonly emptyText = input('Aucune donnée.');
  readonly selectable = input(false);
  readonly selectedKeys = model<string[]>([]);
  readonly actionsTpl =
    contentChild<TemplateRef<{ $implicit: Record<string, unknown> }>>('actions');

  colspan(): number {
    return this.columns().length + (this.actionsTpl() ? 1 : 0) + (this.selectable() ? 1 : 0);
  }

  rowKey(row: Record<string, unknown>): string {
    const v = row[this.trackKey()];
    return v == null ? '' : String(v);
  }

  isSelected(row: Record<string, unknown>): boolean {
    const key = this.rowKey(row);
    return !!key && this.selectedKeys().includes(key);
  }

  allSelected(): boolean {
    const keys = this.rowKeys();
    return keys.length > 0 && keys.every((k) => this.selectedKeys().includes(k));
  }

  someSelected(): boolean {
    if (this.allSelected()) return false;
    return this.rowKeys().some((k) => this.selectedKeys().includes(k));
  }

  toggleAll(event: Event): void {
    const on = (event.target as HTMLInputElement).checked;
    this.selectedKeys.set(on ? this.rowKeys() : []);
  }

  toggleRow(row: Record<string, unknown>): void {
    const key = this.rowKey(row);
    if (!key) return;
    const cur = this.selectedKeys();
    this.selectedKeys.set(cur.includes(key) ? cur.filter((k) => k !== key) : [...cur, key]);
  }

  trackId(row: Record<string, unknown>, index: number): unknown {
    const id = row[this.trackKey()];
    const login = row['login'] ?? row['matricule'] ?? row['sageMatricule'];
    return `${id ?? 'row'}-${login ?? ''}-${index}`;
  }

  display(col: Column, row: Record<string, unknown>): string {
    const value = row[col.key];
    if (col.format) return col.format(value, row);
    if (value === null || value === undefined) return '';
    return String(value);
  }

  private rowKeys(): string[] {
    return this.rows()
      .map((r) => this.rowKey(r))
      .filter((k) => !!k);
  }
}
