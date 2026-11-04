import { NgTemplateOutlet } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { errorMessage } from '../../core/error.logic';
import { ToastService } from '../../core/toast.service';
import { JournalEntry, JournalFacets } from '../../shared/models';
import { JournalService } from '../../shared/services/journal.service';
import { SelectOption, SoftButton, SoftCard, SoftInput, SoftModal, SoftSelect } from '../../shared/components';
import {
  buildCsv,
  buildDetailRows,
  buildQueryParams,
  columnSummary,
  CSV_BOM,
  DEFAULT_PAGE_SIZE,
  defaultFilters,
  displayValue,
  DisplayValue,
  EMPTY_MESSAGE,
  EMPTY_VALUE,
  EXPORT_BLOCK_SIZE,
  EXPORT_LIMIT_MESSAGE,
  exportFileName,
  formatLocalDateTime,
  JournalFilters,
  localDayEndToUtcIso,
  localDayStartToUtcIso,
  normalizeFilters,
  PAGE_SIZES,
  planExport,
  sameOperation,
  totalPages,
  truncateValue,
  validateFilters,
} from './journal.logic';

/**
 * Onglet « Journal » de la page Audit : consultation du journal central des modifications (lecture seule).
 * Filtres, tableau paginé côté serveur, détail avant/après d'une opération et export CSV de la sélection.
 * Le journal est dans la base de l'application : aucune base SAGE active n'est requise.
 */
@Component({
  selector: 'journal-tab',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, SoftButton, SoftCard, SoftInput, SoftSelect, SoftModal, NgTemplateOutlet],
  template: `
    <div class="soft-toolbar journal-filters" role="search" aria-label="Filtres du journal" (keydown.enter)="apply()">
      <label class="soft-field">
        <span class="soft-field__label">Du</span>
        <input class="soft-input" type="date" [(ngModel)]="draft.from" [ngModelOptions]="{ standalone: true }" />
      </label>
      <label class="soft-field">
        <span class="soft-field__label">Au</span>
        <input class="soft-input" type="date" [(ngModel)]="draft.to" [ngModelOptions]="{ standalone: true }" />
      </label>
      <soft-select label="Utilisateur" placeholder="Tous" [options]="userOptions()" [(ngModel)]="draft.user" [ngModelOptions]="{ standalone: true }"></soft-select>
      <soft-select label="Action" placeholder="Toutes" [options]="actionOptions()" [(ngModel)]="draft.action" [ngModelOptions]="{ standalone: true }"></soft-select>
      <soft-select label="Table" placeholder="Toutes" [options]="tableOptions()" [(ngModel)]="draft.table" [ngModelOptions]="{ standalone: true }"></soft-select>
      <soft-input label="Clé (valeur exacte)" placeholder="ex. IDRESPONSABLE=12" [(ngModel)]="draft.key" [ngModelOptions]="{ standalone: true }"></soft-input>
      <soft-input label="Corrélation" placeholder="Identifiant GUID" [(ngModel)]="draft.correlation" [ngModelOptions]="{ standalone: true }"></soft-input>
      <soft-input label="Base SAGE (facultatif)" [(ngModel)]="draft.sageDatabase" [ngModelOptions]="{ standalone: true }"></soft-input>
      <div class="journal-filters__actions">
        <soft-button (click)="apply()">Appliquer</soft-button>
        <soft-button variant="ghost" (click)="reset()">Réinitialiser</soft-button>
        <soft-button variant="secondary" [loading]="exporting()" [disabled]="loading() || total() === 0" (click)="exportCsv()">
          Exporter en CSV
        </soft-button>
      </div>
    </div>
    @if (formError()) {
      <p class="journal-form-error" role="alert">{{ formError() }}</p>
    }

    <soft-card padding="sm">
      @if (loading()) {
        <div class="soft-spinner-lg" role="status" aria-label="Chargement du journal"></div>
      } @else if (error()) {
        <div class="journal-state journal-state--error" role="alert">
          <p>{{ error() }}</p>
          <soft-button variant="secondary" (click)="load()">Réessayer</soft-button>
        </div>
      } @else if (rows().length === 0) {
        <div class="soft-empty" role="status">{{ emptyMessage }}</div>
      } @else {
        <div class="soft-table-wrap journal-wrap">
          <table class="soft-table journal-table">
            <caption class="visually-hidden">Journal des modifications, de la plus récente à la plus ancienne</caption>
            <thead>
              <tr>
                <th scope="col">Date</th>
                <th scope="col">Utilisateur</th>
                <th scope="col">Action</th>
                <th scope="col">Table</th>
                <th scope="col">Clé</th>
                <th scope="col">Colonne</th>
                <th scope="col">Source</th>
                <th scope="col"><span class="visually-hidden">Détail</span></th>
              </tr>
            </thead>
            <tbody>
              @for (row of rows(); track row.id) {
                <tr class="journal-row" (click)="openDetail(row)">
                  <td class="journal-nowrap">{{ dateText(row) }}</td>
                  <td>{{ row.user }}</td>
                  <td>{{ row.action }}</td>
                  <td>{{ row.table }}</td>
                  <td class="journal-wrap-text">{{ valueOrDash(row.key) }}</td>
                  <td class="journal-wrap-text">{{ summary(row) }}</td>
                  <td>{{ row.source }}</td>
                  <td>
                    <button
                      type="button"
                      class="journal-link"
                      (click)="$event.stopPropagation(); openDetail(row)"
                      [attr.aria-label]="'Voir le détail avant/après : ' + row.action + ' ' + row.table + ' ' + (row.key ?? '') + ', ' + dateText(row)"
                    >
                      Détail
                    </button>
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="soft-pagination-wrap">
          <div class="soft-pagination-info" aria-live="polite">{{ rangeInfo() }}</div>
          <label class="soft-pagination-size">
            Lignes par page
            <select [value]="pageSize()" (change)="onPageSize($event)">
              @for (size of pageSizes; track size) {
                <option [value]="size" [selected]="size === pageSize()">{{ size }}</option>
              }
            </select>
          </label>
          <nav class="soft-pagination-nav" aria-label="Pagination du journal">
            <button type="button" class="soft-page-btn" [disabled]="page() <= 1" (click)="goTo(1)" aria-label="Première page">&laquo;</button>
            <button type="button" class="soft-page-btn" [disabled]="page() <= 1" (click)="goTo(page() - 1)" aria-label="Page précédente">&lsaquo;</button>
            <span class="soft-pagination-info">Page {{ page() }} / {{ pages() }}</span>
            <button type="button" class="soft-page-btn" [disabled]="page() >= pages()" (click)="goTo(page() + 1)" aria-label="Page suivante">&rsaquo;</button>
            <button type="button" class="soft-page-btn" [disabled]="page() >= pages()" (click)="goTo(pages())" aria-label="Dernière page">&raquo;</button>
          </nav>
        </div>
      }
    </soft-card>

    <soft-modal [open]="detail() !== null" title="Détail de la modification" size="xl" (closed)="closeDetail()">
      @if (detail(); as d) {
        <dl class="journal-meta">
          <div><dt>Date</dt><dd>{{ dateText(d) }}</dd></div>
          <div><dt>Utilisateur</dt><dd>{{ d.user }}</dd></div>
          <div><dt>Action</dt><dd>{{ d.action }}</dd></div>
          <div><dt>Table</dt><dd>{{ d.table }}</dd></div>
          <div><dt>Clé</dt><dd class="journal-wrap-text">{{ valueOrDash(d.key) }}</dd></div>
          <div><dt>Corrélation</dt><dd class="journal-wrap-text">{{ d.correlation }}</dd></div>
        </dl>
        @if (detailLoading()) {
          <div class="soft-spinner-lg" role="status" aria-label="Chargement du détail"></div>
        } @else {
          @if (detailNote()) {
            <p class="journal-note" role="status">{{ detailNote() }}</p>
          }
          <div class="soft-table-wrap">
            <table class="soft-table journal-table">
              <caption class="visually-hidden">Valeurs avant et après pour cette opération</caption>
              <thead>
                <tr>
                  <th scope="col">Colonne</th>
                  <th scope="col">Avant</th>
                  <th scope="col">Après</th>
                </tr>
              </thead>
              <tbody>
                @for (line of detailRows(); track line.id) {
                  <tr>
                    <th scope="row" class="journal-wrap-text">{{ line.column }}</th>
                    <td><ng-container *ngTemplateOutlet="cell; context: { v: line.before, k: line.id + '-before' }"></ng-container></td>
                    <td><ng-container *ngTemplateOutlet="cell; context: { v: line.after, k: line.id + '-after' }"></ng-container></td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      }
      <div footer>
        <soft-button variant="ghost" (click)="closeDetail()">Fermer</soft-button>
      </div>
    </soft-modal>

    <ng-template #cell let-v="v" let-k="k">
      @if (v.kind === 'masked') {
        <span class="journal-masked">{{ v.text }} <span class="journal-masked__note">({{ v.note }})</span></span>
      } @else if (v.kind === 'empty') {
        <span class="journal-empty">{{ v.text }}<span class="visually-hidden"> (vide)</span></span>
      } @else if (expanded().has(k)) {
        <span class="journal-wrap-text journal-full">{{ v.text }}</span>
        <button type="button" class="journal-link" (click)="toggle(k)">Réduire</button>
      } @else {
        <span class="journal-wrap-text">{{ preview(v).text }}</span>
        @if (preview(v).truncated) {
          <button type="button" class="journal-link" (click)="toggle(k)" aria-label="Afficher le contenu complet">Voir tout</button>
        }
      }
    </ng-template>
  `,
  styles: [
    `
      .journal-filters { align-items: flex-end; }
      .journal-filters__actions { display: flex; gap: 8px; flex-wrap: wrap; align-items: center; }
      .journal-form-error { color: var(--danger); margin: -8px 0 16px; font-size: 0.9rem; }
      .journal-state { padding: 32px 16px; text-align: center; display: grid; gap: 12px; justify-items: center; }
      .journal-state--error { color: var(--danger); background: var(--danger-bg); border: 1px solid var(--danger-border); border-radius: var(--radius-lg); }
      .journal-row { cursor: pointer; }
      .journal-nowrap { white-space: nowrap; }
      .journal-wrap-text { overflow-wrap: anywhere; word-break: break-word; }
      .journal-full { display: block; white-space: pre-wrap; }
      .journal-link {
        border: 0; background: transparent; padding: 4px 8px; cursor: pointer; font: inherit; font-weight: 600;
        color: var(--accent); text-decoration: underline; border-radius: var(--radius-md);
      }
      .journal-link:hover { color: var(--accent-hover); }
      .journal-link:focus-visible, .soft-page-btn:focus-visible, .soft-pagination-size select:focus-visible {
        outline: 2px solid var(--accent); outline-offset: 2px;
      }
      .journal-masked {
        display: inline-block; padding: 2px 8px; border-radius: var(--radius-md);
        border: 1px dashed var(--color-border); background: var(--surface-2); color: var(--text-secondary);
      }
      .journal-masked__note { font-style: italic; }
      .journal-empty { color: var(--text-muted); }
      .journal-meta { display: grid; grid-template-columns: repeat(auto-fit, minmax(200px, 1fr)); gap: 12px; margin: 0 0 16px; }
      .journal-meta div { display: grid; gap: 2px; }
      .journal-meta dt { font-size: 12px; text-transform: uppercase; letter-spacing: 0.05em; color: var(--text-secondary); font-weight: 600; }
      .journal-meta dd { margin: 0; color: var(--app-text); }
      .journal-note { color: var(--text-secondary); margin: 0 0 12px; font-size: 0.9rem; }
      .visually-hidden {
        position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden;
        clip: rect(0, 0, 0, 0); white-space: nowrap; border: 0;
      }
    `,
  ],
})
export class JournalTab implements OnInit {
  private readonly svc = inject(JournalService);
  private readonly toast = inject(ToastService);

  readonly pageSizes = PAGE_SIZES;
  readonly emptyMessage = EMPTY_MESSAGE;

  /** Saisie en cours ; seuls les filtres appliqués (applied) servent aux requêtes, à la pagination et à l'export. */
  draft: JournalFilters = defaultFilters();
  private applied: JournalFilters = defaultFilters();

  readonly rows = signal<JournalEntry[]>([]);
  readonly total = signal(0);
  readonly page = signal(1);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly formError = signal<string | null>(null);
  readonly exporting = signal(false);
  readonly facets = signal<JournalFacets>({ tables: [], actions: [], users: [] });

  readonly detail = signal<JournalEntry | null>(null);
  readonly detailLoading = signal(false);
  readonly detailNote = signal<string | null>(null);
  readonly detailRows = signal<ReturnType<typeof buildDetailRows>>([]);
  readonly expanded = signal<ReadonlySet<string>>(new Set());

  readonly pages = computed(() => totalPages(this.total(), this.pageSize()));
  readonly userOptions = computed(() => toOptions(this.facets().users));
  readonly actionOptions = computed(() => toOptions(this.facets().actions));
  readonly tableOptions = computed(() => toOptions(this.facets().tables));
  readonly rangeInfo = computed(() => {
    const total = this.total();
    if (total === 0) return '0 résultat';
    const start = (this.page() - 1) * this.pageSize() + 1;
    const end = Math.min(this.page() * this.pageSize(), total);
    return `${start}–${end} sur ${total}`;
  });

  private seq = 0;
  private detailSeq = 0;

  ngOnInit(): void {
    this.loadFacets();
    this.load();
  }

  apply(): void {
    const next = normalizeFilters(this.draft);
    const problem = validateFilters(next);
    this.formError.set(problem);
    if (problem) return;
    const periodChanged = next.from !== this.applied.from || next.to !== this.applied.to;
    this.applied = next;
    this.page.set(1);
    if (periodChanged) this.loadFacets();
    this.load();
  }

  reset(): void {
    this.draft = defaultFilters();
    this.formError.set(null);
    this.apply();
  }

  goTo(page: number): void {
    this.page.set(Math.min(Math.max(1, page), this.pages()));
    this.load();
  }

  onPageSize(event: Event): void {
    const size = Number((event.target as HTMLSelectElement).value);
    if (!PAGE_SIZES.includes(size as (typeof PAGE_SIZES)[number])) return;
    this.pageSize.set(size);
    this.page.set(1);
    this.load();
  }

  load(): void {
    const seq = ++this.seq;
    this.loading.set(true);
    this.error.set(null);
    this.svc.list(buildQueryParams(this.applied, this.page(), this.pageSize())).subscribe({
      next: (r) => {
        if (seq !== this.seq) return;
        this.rows.set(r.items);
        this.total.set(r.total);
        this.page.set(r.page);
        this.loading.set(false);
      },
      error: (err: HttpErrorResponse) => {
        if (seq !== this.seq) return;
        this.rows.set([]);
        this.total.set(0);
        this.error.set(errorMessage(err));
        this.loading.set(false);
      },
    });
  }

  private loadFacets(): void {
    this.svc.facets(localDayStartToUtcIso(this.applied.from) ?? undefined, localDayEndToUtcIso(this.applied.to) ?? undefined).subscribe({
      next: (f) => this.facets.set(f),
      // Les listes restent vides : les filtres texte et l'erreur du tableau suffisent, sans second message.
      error: () => this.facets.set({ tables: [], actions: [], users: [] }),
    });
  }

  // ---------- Détail avant/après ----------

  openDetail(row: JournalEntry): void {
    const seq = ++this.detailSeq;
    this.detail.set(row);
    this.detailNote.set(null);
    this.expanded.set(new Set());
    this.detailRows.set(buildDetailRows(this.rows().filter((r) => sameOperation(r, row))));
    this.detailLoading.set(true);
    this.svc
      .list({ correlation: row.correlation, table: row.table, key: row.key ?? undefined, page: 1, pageSize: EXPORT_BLOCK_SIZE })
      .subscribe({
        next: (r) => {
          if (seq !== this.detailSeq) return;
          const lines = r.items.filter((i) => sameOperation(i, row));
          this.detailRows.set(buildDetailRows(lines.length ? lines : [row]));
          if (r.total > r.items.length) this.detailNote.set(`Seules les ${r.items.length} premières lignes de l'opération sont affichées.`);
          this.detailLoading.set(false);
        },
        error: () => {
          if (seq !== this.detailSeq) return;
          this.detailNote.set("Le détail complet n'a pas pu être chargé : seules les lignes de la page sont affichées.");
          this.detailLoading.set(false);
        },
      });
  }

  closeDetail(): void {
    this.detailSeq++;
    this.detail.set(null);
  }

  toggle(key: string): void {
    const next = new Set(this.expanded());
    if (next.has(key)) next.delete(key);
    else next.add(key);
    this.expanded.set(next);
  }

  preview(value: DisplayValue): { text: string; truncated: boolean } {
    return truncateValue(value.text);
  }

  // ---------- Export CSV ----------

  async exportCsv(): Promise<void> {
    if (this.exporting()) return;
    this.exporting.set(true);
    try {
      const first = await firstValueFrom(this.svc.list(buildQueryParams(this.applied, 1, EXPORT_BLOCK_SIZE)));
      const plan = planExport(first.total);
      if (plan.rows === 0) {
        this.toast.info(EMPTY_MESSAGE);
        return;
      }
      const entries = [...first.items];
      for (let block = 2; block <= plan.blocks; block++) {
        const next = await firstValueFrom(this.svc.list(buildQueryParams(this.applied, block, EXPORT_BLOCK_SIZE)));
        entries.push(...next.items);
      }
      download(CSV_BOM + buildCsv(entries.slice(0, plan.rows)), exportFileName());
      if (plan.limited) this.toast.warning(EXPORT_LIMIT_MESSAGE);
      else this.toast.success(`${plan.rows} ligne(s) exportée(s).`);
    } catch {
      // Le message d'erreur est déjà affiché par l'intercepteur HTTP.
    } finally {
      this.exporting.set(false);
    }
  }

  // ---------- Affichage ----------

  dateText(row: JournalEntry): string {
    return formatLocalDateTime(row.dateUtc);
  }

  summary(row: JournalEntry): string {
    return columnSummary(row);
  }

  valueOrDash(value: string | null): string {
    return displayValue(value).kind === 'empty' ? EMPTY_VALUE : (value as string);
  }
}

function toOptions(values: readonly string[]): SelectOption[] {
  return values.map((v) => ({ value: v, label: v }));
}

function download(content: string, fileName: string): void {
  const url = URL.createObjectURL(new Blob([content], { type: 'text/csv;charset=utf-8' }));
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}
