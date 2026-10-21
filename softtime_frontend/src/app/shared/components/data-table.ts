import {
  ChangeDetectionStrategy,
  Component,
  computed,
  contentChild,
  input,
  model,
  output,
  signal,
  TemplateRef,
} from '@angular/core';

/** Compteur statique pour générer un id unique par instance du composant. */
let _dataTableInstanceCounter = 0;
import { CommonModule } from '@angular/common';
import { PageChangeEvent } from '../models/pagination.models';

export interface Column {
  key: string;
  label: string;
  align?: 'left' | 'right' | 'center';
  /** Optional value formatter. Receives the raw cell value and full row. */
  format?: (value: unknown, row: Record<string, unknown>) => string;
}

/** Tailles de page proposées en mode serveur (pas d'option "tout" : règle non confirmée). */
const SERVER_PAGE_SIZES = [10, 25, 50, 100] as const;

@Component({
  selector: 'data-table',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule],
  template: `
    <!-- =====================================================================
         MODE CLIENT (serverSide = false, par défaut)
         Comportement strictement identique à l'existant : slice côté client,
         taille de page = pageSize() (défaut 15), compteurs sur rows().length.
         ===================================================================== -->
    @if (!serverSide()) {
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

        <div class="soft-table-cards">
          @for (row of paginatedRows(); track trackId(row, $index)) {
            <article class="soft-table-card" [class.is-selected]="selectable() && isSelected(row)">
              @if (selectable()) {
                <label class="soft-table-card__select">
                  <input
                    type="checkbox"
                    [checked]="isSelected(row)"
                    (change)="toggleRow(row)"
                    [attr.aria-label]="'Sélectionner ' + rowKey(row)"
                  />
                  <span>Sélectionner</span>
                </label>
              }
              @for (col of columns(); track col.key) {
                <div class="soft-table-card__field">
                  <span>{{ col.label }}</span>
                  <strong [class.text-right]="col.align === 'right'" [class.text-center]="col.align === 'center'">
                    {{ display(col, row) }}
                  </strong>
                </div>
              }
              @if (actionsTpl(); as tpl) {
                <div class="soft-table-card__actions">
                  <span>Actions</span>
                  <div>
                    <ng-container
                      [ngTemplateOutlet]="tpl"
                      [ngTemplateOutletContext]="{ $implicit: row }"
                    ></ng-container>
                  </div>
                </div>
              }
            </article>
          } @empty {
            <p class="soft-table-card__empty">{{ emptyText() }}</p>
          }
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
    }

    <!-- =====================================================================
         MODE SERVEUR (serverSide = true)
         Les données reçues dans [rows] représentent UNIQUEMENT la page courante.
         La pagination (Skip/Take) est effectuée par le backend.
         [total]          = nombre total d'éléments (PagedResult.total).
         [(serverPage)]   = page courante, liaison bidirectionnelle avec le parent.
                            Le parent peut la remettre à 1 lors d'un filtre :
                            [(serverPage)]="currentPage" [(serverPageSize)]="pageSize"
                            (pageChange)="onPageChange($event)"
         [(serverPageSize)] = taille de page, liaison bidirectionnelle.
         (pageChange)     = émis à chaque navigation ou changement de taille.
         SÉLECTION : limitée à la page courante. Lorsque le backend renvoie une nouvelle
         page, les clés sélectionnées précédentes peuvent ne plus correspondre à des
         lignes visibles. Ce comportement est volontaire : la sélection inter-pages
         n'est pas gérée en mode serveur pour éviter des incohérences d'état.
         ===================================================================== -->
    @if (serverSide()) {
      <!-- Barre de pagination TOUJOURS affichée en mode serveur (pas de clignotement).
           Seul le corps du tableau est remplacé par le spinner pendant le chargement. -->
      <div class="soft-pagination-wrap">
        <div class="soft-pagination-info">{{ serverRangeInfo() }}</div>

        <!-- Sélecteur de taille de page (options 10/25/50/100 uniquement).
             id unique par instance via _instanceId. Utilise (change) natif (pas FormsModule). -->
        <div class="soft-pagination-size">
          <label [for]="pageSizeSelectId">Lignes par page&nbsp;:</label>
          <select
            [id]="pageSizeSelectId"
            class="soft-select soft-select--sm"
            (change)="onServerPageSizeChange($event)"
          >
            @for (size of pageSizes; track size) {
              <option [value]="size" [selected]="size === serverPageSize()">{{ size }}</option>
            }
          </select>
        </div>

        @if (serverTotalPages() > 1) {
          <nav class="soft-pagination-nav" aria-label="Pagination">
            <button
              type="button"
              class="soft-page-btn"
              [disabled]="serverPage() === 1"
              (click)="serverGoToPage(1)"
              aria-label="Première page"
              title="Première page"
            >
              &laquo;
            </button>
            <button
              type="button"
              class="soft-page-btn"
              [disabled]="serverPage() === 1"
              (click)="serverGoToPage(serverPage() - 1)"
              aria-label="Page précédente"
              title="Page précédente"
            >
              &lsaquo;
            </button>

            @for (item of serverPaginationItems(); track $index) {
              @if (item === '...') {
                <span class="soft-page-ellipsis">&hellip;</span>
              } @else {
                <button
                  type="button"
                  class="soft-page-btn"
                  [class.is-active]="item === serverPage()"
                  [attr.aria-current]="item === serverPage() ? 'page' : null"
                  (click)="serverGoToPage(item)"
                  [attr.aria-label]="'Page ' + item"
                >
                  {{ item }}
                </button>
              }
            }

            <button
              type="button"
              class="soft-page-btn"
              [disabled]="serverPage() === serverTotalPages()"
              (click)="serverGoToPage(serverPage() + 1)"
              aria-label="Page suivante"
              title="Page suivante"
            >
              &rsaquo;
            </button>
            <button
              type="button"
              class="soft-page-btn"
              [disabled]="serverPage() === serverTotalPages()"
              (click)="serverGoToPage(serverTotalPages())"
              aria-label="Dernière page"
              title="Dernière page"
            >
              &raquo;
            </button>
          </nav>
        }
      </div>

      <!-- Corps : spinner pendant le chargement, tableau sinon. La barre reste stable. -->
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

        <div class="soft-table-cards">
          @for (row of rows(); track trackId(row, $index)) {
            <article class="soft-table-card" [class.is-selected]="selectable() && isSelected(row)">
              @if (selectable()) {
                <label class="soft-table-card__select">
                  <input
                    type="checkbox"
                    [checked]="isSelected(row)"
                    (change)="toggleRow(row)"
                    [attr.aria-label]="'Sélectionner ' + rowKey(row)"
                  />
                  <span>Sélectionner</span>
                </label>
              }
              @for (col of columns(); track col.key) {
                <div class="soft-table-card__field">
                  <span>{{ col.label }}</span>
                  <strong [class.text-right]="col.align === 'right'" [class.text-center]="col.align === 'center'">
                    {{ display(col, row) }}
                  </strong>
                </div>
              }
              @if (actionsTpl(); as tpl) {
                <div class="soft-table-card__actions">
                  <span>Actions</span>
                  <div>
                    <ng-container
                      [ngTemplateOutlet]="tpl"
                      [ngTemplateOutletContext]="{ $implicit: row }"
                    ></ng-container>
                  </div>
                </div>
              }
            </article>
          } @empty {
            <p class="soft-table-card__empty">{{ emptyText() }}</p>
          }
        </div>
      }
    }
  `,
})
export class DataTable {
  // ──────────────────────────────────────────────────────────────────────────
  // Inputs communs (modes client ET serveur)
  // ──────────────────────────────────────────────────────────────────────────
  readonly columns = input<Column[]>([]);
  readonly rows = input<Record<string, unknown>[]>([]);
  readonly loading = input(false);
  readonly trackKey = input('id');
  readonly emptyText = input('Aucune donnée.');
  readonly selectable = input(false);
  readonly selectedKeys = model<string[]>([]);
  readonly actionsTpl =
    contentChild<TemplateRef<{ $implicit: Record<string, unknown> }>>('actions');

  // ──────────────────────────────────────────────────────────────────────────
  // Inputs mode CLIENT uniquement
  // ──────────────────────────────────────────────────────────────────────────
  /** Active la pagination côté client. Défaut true, inchangé. */
  readonly paginate = input<boolean>(true);
  /** Taille de page en mode client. Défaut 15, inchangé. */
  readonly pageSize = input<number>(15);
  readonly currentPage = signal<number>(1);

  // ──────────────────────────────────────────────────────────────────────────
  // Inputs mode SERVEUR (ignorés lorsque serverSide = false)
  // ──────────────────────────────────────────────────────────────────────────
  /**
   * Active le mode serveur. Défaut false.
   * En mode serveur : [rows] contient uniquement la page courante reçue du backend,
   * [total] est le nombre total d'éléments, (pageChange) est émis à chaque navigation.
   *
   * Exemple de liaison dans une page :
   *   <data-table
   *     [serverSide]="true"
   *     [rows]="rows()"
   *     [total]="total()"
   *     [loading]="loading()"
   *     [(serverPage)]="currentPage"
   *     [(serverPageSize)]="pageSize"
   *     (pageChange)="onPageChange($event)"
   *   ></data-table>
   *
   * Le parent peut remettre currentPage à 1 lors d'un filtre :
   *   applyFilter() { this.currentPage.set(1); this.load(); }
   */
  readonly serverSide = input(false);
  /** Nombre total d'éléments retourné par le backend (PagedResult.total). Mode serveur uniquement. */
  readonly total = input(0);

  // ──────────────────────────────────────────────────────────────────────────
  // Modèles bidirectionnels mode SERVEUR
  // Permettent [(serverPage)]="..." et [(serverPageSize)]="..." depuis le parent.
  // Le parent peut remettre serverPage à 1 lors d'un changement de filtre sans
  // passer par (pageChange) : le tableau s'affiche alors sur la page 1.
  // ──────────────────────────────────────────────────────────────────────────
  /** Page courante (1-based). Liaison bidirectionnelle : [(serverPage)]="currentPage". */
  readonly serverPage = model(1);
  /** Taille de page active. Liaison bidirectionnelle : [(serverPageSize)]="pageSize". Options : 10/25/50/100. */
  readonly serverPageSize = model(10);

  // ──────────────────────────────────────────────────────────────────────────
  // Output mode SERVEUR
  // ──────────────────────────────────────────────────────────────────────────
  /** Émis lors de chaque changement de page ou de taille de page en mode serveur. */
  readonly pageChange = output<PageChangeEvent>();

  // ──────────────────────────────────────────────────────────────────────────
  // Id unique par instance (évite les doublons d'id HTML entre plusieurs data-table sur la même page)
  // ──────────────────────────────────────────────────────────────────────────
  readonly pageSizes = SERVER_PAGE_SIZES;
  readonly pageSizeSelectId = `data-table-page-size-${++_dataTableInstanceCounter}`;

  // ──────────────────────────────────────────────────────────────────────────
  // Signaux calculés — mode CLIENT (identiques à l'existant)
  // ──────────────────────────────────────────────────────────────────────────
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

  // ──────────────────────────────────────────────────────────────────────────
  // Signaux calculés — mode SERVEUR
  // ──────────────────────────────────────────────────────────────────────────
  readonly serverTotalPages = computed(() => {
    const t = this.total();
    const s = this.serverPageSize();
    if (s <= 0 || t <= 0) return 1;
    return Math.max(1, Math.ceil(t / s));
  });

  readonly serverRangeInfo = computed(() => {
    const t = this.total();
    if (t === 0) return '0 résultat';
    const s = this.serverPageSize();
    const p = this.serverPage();
    const start = (p - 1) * s + 1;
    const end = Math.min(p * s, t);
    return `Affichage de ${start} à ${end} sur ${t} résultat${t > 1 ? 's' : ''}`;
  });

  readonly serverPaginationItems = computed(() => {
    return this.getPaginationItems(this.serverPage(), this.serverTotalPages());
  });

  // ──────────────────────────────────────────────────────────────────────────
  // Méthodes — mode CLIENT (identiques à l'existant)
  // ──────────────────────────────────────────────────────────────────────────
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

  // ──────────────────────────────────────────────────────────────────────────
  // Méthodes — mode SERVEUR
  // ──────────────────────────────────────────────────────────────────────────
  serverGoToPage(p: number | '...'): void {
    if (typeof p !== 'number') return;
    const target = Math.max(1, Math.min(p, this.serverTotalPages()));
    this.serverPage.set(target);
    this.pageChange.emit({ page: target, pageSize: this.serverPageSize() });
  }

  onServerPageSizeChange(event: Event): void {
    const newSize = Number((event.target as HTMLSelectElement).value);
    // Retour à la page 1 lors d'un changement de taille (comportement standard).
    this.serverPageSize.set(newSize);
    this.serverPage.set(1);
    this.pageChange.emit({ page: 1, pageSize: newSize });
  }
}
