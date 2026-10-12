import { ChangeDetectionStrategy, Component, computed, contentChild, input, model, signal, TemplateRef } from '@angular/core';
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
            @for (row of paginatedRows(); track trackId(row, $index)) {
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

      @if (paginate() && rows().length > 0) {
        <div class="soft-pagination-wrap">
          <div class="soft-pagination-info">{{ rangeInfo() }}</div>
          @if (totalPages() > 1) {
            <nav class="soft-pagination-nav" aria-label="Pagination">
              <button
                type="button"
                class="soft-page-btn"
                [disabled]="safeCurrentPage() === 1"
                (click)="goToPage(1)"
                aria-label="Première page"
                title="Première page"
              >
                &laquo;
              </button>
              <button
                type="button"
                class="soft-page-btn"
                [disabled]="safeCurrentPage() === 1"
                (click)="goToPage(safeCurrentPage() - 1)"
                aria-label="Page précédente"
                title="Page précédente"
              >
                &lsaquo;
              </button>

              @for (item of paginationItems(); track $index) {
                @if (item === '...') {
                  <span class="soft-page-ellipsis">&hellip;</span>
                } @else {
                  <button
                    type="button"
                    class="soft-page-btn"
                    [class.is-active]="item === safeCurrentPage()"
                    [attr.aria-current]="item === safeCurrentPage() ? 'page' : null"
                    (click)="goToPage(item)"
                    [attr.aria-label]="'Page ' + item"
                  >
                    {{ item }}
                  </button>
                }
              }

              <button
                type="button"
                class="soft-page-btn"
                [disabled]="safeCurrentPage() === totalPages()"
                (click)="goToPage(safeCurrentPage() + 1)"
                aria-label="Page suivante"
                title="Page suivante"
              >
                &rsaquo;
              </button>
              <button
                type="button"
                class="soft-page-btn"
                [disabled]="safeCurrentPage() === totalPages()"
                (click)="goToPage(totalPages())"
                aria-label="Dernière page"
                title="Dernière page"
              >
                &raquo;
              </button>
            </nav>
          }
        </div>
      }
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

  readonly paginate = input<boolean>(true);
  readonly pageSize = input<number>(15);
  readonly currentPage = signal<number>(1);

  readonly totalRows = computed(() => this.rows().length);
  readonly totalPages = computed(() => {
    const size = this.pageSize();
    if (size <= 0) return 1;
    return Math.max(1, Math.ceil(this.totalRows() / size));
  });

  readonly safeCurrentPage = computed(() => {
    const cur = this.currentPage();
    const max = this.totalPages();
    if (cur < 1) return 1;
    if (cur > max) return max;
    return cur;
  });

  readonly paginatedRows = computed(() => {
    const all = this.rows();
    if (!this.paginate()) return all;
    const size = this.pageSize();
    if (size <= 0) return all;
    const page = this.safeCurrentPage();
    const start = (page - 1) * size;
    return all.slice(start, start + size);
  });

  readonly rangeInfo = computed(() => {
    const total = this.totalRows();
    if (total === 0) return '0 résultat';
    if (!this.paginate()) return `${total} résultat${total > 1 ? 's' : ''}`;
    const size = this.pageSize();
    const page = this.safeCurrentPage();
    const start = (page - 1) * size + 1;
    const end = Math.min(page * size, total);
    return `Affichage de ${start} à ${end} sur ${total} résultat${total > 1 ? 's' : ''}`;
  });

  readonly paginationItems = computed(() => {
    const total = this.totalPages();
    const current = this.safeCurrentPage();
    return this.getPaginationItems(current, total);
  });

  private getPaginationItems(current: number, total: number): (number | '...')[] {
    if (total <= 7) {
      return Array.from({ length: total }, (_, i) => i + 1);
    }
    const items: (number | '...')[] = [];
    if (current <= 4) {
      for (let i = 1; i <= 5; i++) {
        items.push(i);
      }
      items.push('...');
      items.push(total);
    } else if (current >= total - 3) {
      items.push(1);
      items.push('...');
      for (let i = total - 4; i <= total; i++) {
        items.push(i);
      }
    } else {
      items.push(1);
      items.push('...');
      for (let i = current - 2; i <= current + 2; i++) {
        items.push(i);
      }
      items.push('...');
      items.push(total);
    }
    return items;
  }

  goToPage(p: number | '...'): void {
    if (typeof p !== 'number') return;
    const target = Math.max(1, Math.min(p, this.totalPages()));
    this.currentPage.set(target);
  }

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
