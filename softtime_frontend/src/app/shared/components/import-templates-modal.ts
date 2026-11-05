import {
  ChangeDetectionStrategy,
  Component,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { ToastService } from '../../core/toast.service';
import { ImportTemplate } from '../models';
import { ImportTemplateService } from '../services/import-template.service';
import { SoftBadge } from './soft-badge';
import { SoftButton } from './soft-button';
import { SoftModal } from './soft-modal';

/**
 * Aide à l'import : pour chaque import de l'écran, affiche la matrice des colonnes attendues,
 * un exemple de fichier et les règles, avec le modèle téléchargeable (.xlsx / .csv).
 * Les définitions viennent du backend (alignées sur les parseurs d'import).
 */
@Component({
  selector: 'app-import-templates-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SoftModal, SoftButton, SoftBadge],
  template: `
    <soft-modal [open]="open()" [title]="title()" size="lg" (closed)="closed.emit()">
      @if (loading()) {
        <div class="soft-spinner-lg"></div>
      } @else if (templates().length === 0) {
        <p class="hint">Aucun modèle d'import disponible pour cet écran.</p>
      } @else {
        @for (template of templates(); track template.key) {
          <section class="template">
            <header class="template-head">
              <div>
                <h4>{{ template.label }}</h4>
                <p class="desc">{{ template.description }}</p>
              </div>
              <div class="actions">
                <soft-badge [variant]="template.format === 'xlsx' ? 'info' : 'muted'">
                  {{ template.format.toUpperCase() }}
                </soft-badge>
                <soft-button size="sm" (click)="download(template)">Télécharger le modèle</soft-button>
              </div>
            </header>

            <h5>Colonnes attendues</h5>
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    <th>Colonne</th>
                    <th>Obligatoire</th>
                    <th>Format</th>
                    <th>Exemple</th>
                    <th>Remarque</th>
                  </tr>
                </thead>
                <tbody>
                  @for (column of template.columns; track column.header) {
                    <tr>
                      <td><code>{{ column.header }}</code></td>
                      <td>{{ column.required ? 'Oui' : 'Non' }}</td>
                      <td>{{ column.format }}</td>
                      <td>{{ column.example }}</td>
                      <td class="note">{{ column.note ?? '' }}</td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <h5>Exemple de fichier — {{ template.fileName }}</h5>
            <div class="table-wrap">
              <table>
                <thead>
                  <tr>
                    @for (header of headers(template); track $index) {
                      <th>{{ header }}</th>
                    }
                  </tr>
                </thead>
                <tbody>
                  @for (row of template.exampleRows; track $index) {
                    <tr>
                      @for (cell of row; track $index) {
                        <td>{{ cell }}</td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>

            <ul class="notes">
              @for (note of template.notes; track $index) {
                <li>{{ note }}</li>
              }
            </ul>
          </section>
        }
      }
      <div footer>
        <soft-button variant="ghost" (click)="closed.emit()">Fermer</soft-button>
      </div>
    </soft-modal>
  `,
  styles: [
    `
      .hint { margin: 0; color: var(--text-muted); }
      .template { padding: 16px 0; border-bottom: 1px solid var(--color-border); }
      .template:last-of-type { border-bottom: 0; }
      .template-head { display: flex; align-items: flex-start; justify-content: space-between; gap: 16px; }
      .template-head h4 { margin: 0; font-family: var(--font-heading); font-size: 15px; }
      .desc { margin: 4px 0 0; color: var(--text-muted); font-size: 13px; }
      .actions { display: flex; align-items: center; gap: 10px; flex-shrink: 0; }
      h5 { margin: 16px 0 8px; font-size: 13px; text-transform: uppercase; letter-spacing: 0.04em; color: var(--text-muted); }
      .table-wrap { overflow-x: auto; }
      table { width: 100%; border-collapse: collapse; font-size: 13px; }
      th, td { border: 1px solid var(--color-border); padding: 6px 8px; text-align: left; vertical-align: top; white-space: nowrap; }
      th { background: var(--surface-light); font-weight: 600; }
      td.note { white-space: normal; color: var(--text-muted); }
      code { font-family: var(--font-mono, monospace); font-size: 12.5px; }
      .notes { margin: 10px 0 0; padding-left: 18px; color: var(--text-muted); font-size: 13px; }
      .notes li { margin: 2px 0; }
    `,
  ],
})
export class ImportTemplatesModal {
  readonly open = input(false);
  /** Clés des modèles à afficher, dans l'ordre. Vide = tous. */
  readonly keys = input<string[]>([]);
  readonly title = input("Modèles d'import");
  readonly closed = output<void>();

  private readonly svc = inject(ImportTemplateService);
  private readonly toast = inject(ToastService);

  readonly loading = signal(false);
  private readonly all = signal<ImportTemplate[]>([]);

  readonly templates = computed(() => {
    const wanted = this.keys();
    const all = this.all();
    if (wanted.length === 0) return all;
    return wanted
      .map((key) => all.find((template) => template.key === key))
      .filter((template): template is ImportTemplate => !!template);
  });

  constructor() {
    effect(() => {
      if (!this.open() || this.all().length > 0 || this.loading()) return;
      this.loading.set(true);
      this.svc.list().subscribe({
        next: (list) => {
          this.all.set(list);
          this.loading.set(false);
        },
        error: () => {
          this.loading.set(false);
          this.toast.error("Modèles d'import indisponibles.");
        },
      });
    });
  }

  headers(template: ImportTemplate): string[] {
    return template.columns.map((column) => column.header);
  }

  download(template: ImportTemplate): void {
    this.svc.download(template).subscribe({
      next: (blob) => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = template.fileName;
        anchor.click();
        URL.revokeObjectURL(url);
      },
      error: () => this.toast.error('Téléchargement du modèle impossible.'),
    });
  }
}
