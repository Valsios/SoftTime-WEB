import { ChangeDetectionStrategy, Component, computed, inject, OnInit, output, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { errorMessage } from '../../core/error.logic';
import { ToastService } from '../../core/toast.service';
import { PayrollTargetColumn, PayrollWriteTargets } from '../../shared/models';
import { PayrollVariablesService } from '../../shared/services/payroll-variables.service';
import { SoftButton } from '../../shared/components';
import {
  changementsDestinations,
  ChoixColonne,
  choixColonnes,
  DestinationsBrouillon,
  validerDestinations,
  versDestinations,
} from './payroll-variables.logic';

/**
 * Autre base : « Destinations dans la base de paie » (T_PAIE_ECRITURE). Table, colonne matricule et colonne de chaque
 * variable envoyable, choisies dans des listes lues dans la base de paie. Rien n'est envoyé avant la confirmation ; le
 * serveur vérifie de nouveau la table et les colonnes (existence, type numérique, décimales de Retard).
 */
@Component({
  selector: 'app-payroll-destinations',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SoftButton],
  template: `
    <section class="pd">
      <div class="pd-head">
        <h3 class="pd-title">Destinations dans la base de paie</h3>
        @if (config() && !config()!.moisEnvoye) {
          <div class="pd-actions">
            <soft-button variant="ghost" [disabled]="!modifie() || saving()" (click)="annuler()">Annuler les changements</soft-button>
            <soft-button [loading]="saving()" [disabled]="!modifie()" (click)="enregistrer()">Enregistrer les destinations</soft-button>
          </div>
        }
      </div>
      <p class="pd-note">
        Chaque variable envoyée est écrite dans une colonne de la table choisie, sur la ligne du salarié repérée par la
        colonne matricule. Seules les colonnes numériques sont utilisables ; Retard (en heures, avec décimales) exige une
        colonne qui accepte deux décimales.
      </p>
      @if (loading()) {
        <p class="pd-note">Chargement…</p>
      } @else if (brouillon(); as d) {
        @if (config()!.moisEnvoye) {
          <p class="pd-banner pd-banner--warning" role="status">
            Mois déjà envoyé en paie (période {{ config()!.dernierEnvoi }}) : modification impossible.
          </p>
        }
        @if (erreurs().length) {
          <div class="pd-banner pd-banner--danger" role="alert">
            @for (e of erreurs(); track $index) { <p>{{ e }}</p> }
          </div>
        }
        @if (!config()!.tables.length) {
          <p class="pd-note">La liste des tables de la base de paie n'a pas pu être lue.</p>
        }
        <div class="pd-fields">
          <label class="soft-field pd-field">
            <span class="soft-field__label">Table</span>
            <span class="soft-select"><span class="soft-select__control">
              <select aria-label="Table de destination" [disabled]="bloque()"
                (change)="choisirTable($any($event.target).value)">
                <option value="" [selected]="!d.table">(aucune)</option>
                @for (t of tables(); track t) { <option [value]="t" [selected]="t === d.table">{{ t }}</option> }
              </select>
            </span></span>
          </label>
          <label class="soft-field pd-field">
            <span class="soft-field__label">Colonne matricule</span>
            <span class="soft-select"><span class="soft-select__control">
              <select aria-label="Colonne matricule" [disabled]="bloque() || !d.table"
                (change)="choisirMatricule($any($event.target).value)">
                <option value="" [selected]="!d.colonneMatricule">(aucune)</option>
                @for (c of colonnesMatricule(); track c) { <option [value]="c" [selected]="c === d.colonneMatricule">{{ c }}</option> }
              </select>
            </span></span>
          </label>
        </div>
        <div class="soft-table-wrap pd-wrap">
        <table class="soft-table pd-table">
          <thead>
            <tr><th>Variable</th><th>Envoyée</th><th>Colonne</th></tr>
          </thead>
          <tbody>
            @for (v of d.variables; track v.variable) {
              <tr>
                <td class="pd-name">{{ v.libelle }}</td>
                <td data-label="Envoyée">{{ v.actif ? 'oui' : 'non' }}</td>
                <td data-label="Colonne">
                  <div class="soft-select pd-select"><div class="soft-select__control">
                    <select [attr.aria-label]="'Colonne de ' + v.libelle" [disabled]="bloque() || !d.table"
                      (change)="choisirColonne(v.variable, $any($event.target).value)">
                      <option value="" [selected]="!v.colonne">(aucune)</option>
                      @for (c of choix(v.decimalesRequises, v.colonne); track c.nom) {
                        <option [value]="c.nom" [disabled]="!c.utilisable" [selected]="c.nom === v.colonne">{{ c.libelle }}</option>
                      }
                    </select>
                  </div></div>
                </td>
              </tr>
            }
          </tbody>
        </table>
        </div>
      }
    </section>
  `,
  styles: [`
    .pd { margin-top: var(--space-xl); padding-top: var(--space-lg); border-top: 1px solid var(--border-subtle); }
    .pd-head { display: flex; justify-content: space-between; align-items: flex-start; gap: var(--space-md); flex-wrap: wrap; }
    .pd-title { margin: 0; font-family: var(--font-heading); font-size: 16px; font-weight: 600; color: var(--app-text-strong); }
    .pd-actions { display: flex; gap: var(--space-sm); flex-wrap: wrap; }
    .pd-note { color: var(--text-muted); margin: var(--space-md) 0; font-size: 13px; }
    .pd-banner { margin: var(--space-md) 0; padding: 10px 14px; font-size: 13px; border-radius: var(--radius-md); border: 1px solid; }
    .pd-banner p { margin: 2px 0; }
    .pd-banner--danger { color: var(--danger); background: var(--danger-bg); border-color: var(--danger-border); }
    .pd-banner--warning { color: var(--warning); background: var(--warning-bg); border-color: var(--warning-border); }
    .pd-fields { display: flex; gap: var(--space-lg); flex-wrap: wrap; margin: var(--space-md) 0; }
    .pd-field { min-width: 200px; max-width: 280px; flex: 1 1 200px; }
    .pd-select { min-width: 200px; max-width: 320px; }
    .pd select:disabled { background: var(--surface-dark); color: var(--text-muted); cursor: not-allowed; }
    .pd-table td { vertical-align: top; }
    /* Largeur étroite : la règle globale masque .soft-table-wrap ; le tableau des destinations est présenté en cartes
       (une par variable, comme .soft-table-card), avec les mêmes listes et les mêmes états désactivés. */
    @media (max-width: 767px) {
      .soft-table-wrap.pd-wrap { display: block; overflow: visible; border: 0; border-radius: 0; box-shadow: none; background: transparent; }
      .pd-table, .pd-table tbody, .pd-table tr, .pd-table td { display: block; }
      .pd-table thead { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
      .pd-table tbody { display: grid; gap: 12px; }
      .pd-table tbody tr { display: grid; gap: 10px; padding: 16px; border: 1px solid var(--color-border); border-radius: var(--radius-lg);
        background: var(--app-surface); box-shadow: var(--shadow-xs); }
      .pd-table tbody tr:hover { background: var(--app-surface); }
      .pd-table tbody td { display: grid; grid-template-columns: minmax(0, 42%) minmax(0, 1fr); gap: 4px 12px; align-items: center;
        min-width: 0; padding: 0; border: 0; }
      .pd-table tbody td::before { content: attr(data-label); color: var(--text-secondary); font-size: 12px; font-weight: 600; }
      .pd-table tbody td.pd-name { display: block; font-weight: 600; }
      .pd-table tbody td.pd-name::before { content: none; }
      /* Liste de colonne : étiquette au-dessus, liste sur toute la largeur (libellés « colonne (type) » longs). */
      .pd-table tbody td:has(.pd-select) { grid-template-columns: minmax(0, 1fr); }
      .pd-select { min-width: 0; max-width: none; width: 100%; }
      .pd-field { min-width: 0; max-width: none; flex-basis: 100%; }
    }
  `],
})
export class PayrollDestinations implements OnInit {
  private readonly svc = inject(PayrollVariablesService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  /** Destinations enregistrées : la carte relit la configuration des variables. */
  readonly enregistre = output<void>();

  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly config = signal<PayrollWriteTargets | null>(null);
  readonly brouillon = signal<DestinationsBrouillon | null>(null);
  /** Colonnes de la table choisie dans le brouillon. */
  readonly colonnes = signal<PayrollTargetColumn[]>([]);
  readonly erreurs = signal<string[]>([]);

  readonly bloque = computed(() => (this.config()?.moisEnvoye ?? true) || this.saving());
  readonly modifie = computed(() => {
    const c = this.config();
    const d = this.brouillon();
    return !!c && !!d && changementsDestinations(this.initial(c), d).length > 0;
  });
  /** Tables proposées ; la table enregistrée reste visible même si la liste n'a pas pu être lue. */
  readonly tables = computed(() => {
    const liste = this.config()?.tables ?? [];
    const table = this.brouillon()?.table;
    return table && !liste.some((t) => t.toLowerCase() === table.toLowerCase()) ? [table, ...liste] : liste;
  });
  readonly colonnesMatricule = computed(() => {
    const liste = this.colonnes().map((c) => c.nom);
    const actuelle = this.brouillon()?.colonneMatricule;
    return actuelle && !liste.some((c) => c.toLowerCase() === actuelle.toLowerCase()) ? [actuelle, ...liste] : liste;
  });

  ngOnInit(): void {
    this.charger();
  }

  charger(): void {
    this.loading.set(true);
    this.svc.destinations().subscribe({
      next: (c) => this.appliquer(c),
      error: (e) => {
        this.loading.set(false);
        this.erreurs.set([errorMessage(e)]);
      },
    });
  }

  choix(decimalesRequises: number, actuelle: string | null): ChoixColonne[] {
    return choixColonnes(this.colonnes(), decimalesRequises, actuelle);
  }

  /** Nouvelle table : matricule et colonnes sont à choisir de nouveau dans ses colonnes. */
  choisirTable(table: string): void {
    const t = table.trim() || null;
    this.maj((d) => ({ table: t, colonneMatricule: null, variables: d.variables.map((v) => ({ ...v, colonne: null })) }));
    this.colonnes.set([]);
    if (!t) return;
    this.svc.colonnes(t).subscribe({
      next: (colonnes) => this.colonnes.set(colonnes),
      error: (e) => this.erreurs.set([errorMessage(e)]),
    });
  }

  choisirMatricule(colonne: string): void {
    this.maj((d) => ({ ...d, colonneMatricule: colonne.trim() || null }));
  }

  choisirColonne(variable: string, colonne: string): void {
    this.maj((d) => ({
      ...d,
      variables: d.variables.map((v) => (v.variable === variable ? { ...v, colonne: colonne.trim() || null } : v)),
    }));
  }

  annuler(): void {
    const c = this.config();
    if (c) this.appliquer(c);
  }

  async enregistrer(): Promise<void> {
    const c = this.config();
    const d = this.brouillon();
    if (!c || !d || this.saving() || c.moisEnvoye) return;
    const erreurs = validerDestinations(d);
    this.erreurs.set(erreurs);
    if (erreurs.length) return;
    const liste = changementsDestinations(this.initial(c), d);
    if (!liste.length) return;
    const question = `Enregistrer les destinations de la base ${c.base} : ${liste.join(' ; ')} ? Le changement s'appliquera au prochain envoi en paie.`;
    if (!(await this.confirm.ask(question, 'Destinations dans la base de paie', 'Enregistrer'))) return;
    this.saving.set(true);
    this.svc.saveDestinations(versDestinations(d)).subscribe({
      next: (resultat) => {
        this.saving.set(false);
        this.appliquer(resultat);
        this.toast.success('Destinations enregistrées.');
        this.enregistre.emit();
      },
      error: (e) => {
        this.saving.set(false);
        this.erreurs.set([errorMessage(e)]);
      },
    });
  }

  private initial(c: PayrollWriteTargets): DestinationsBrouillon {
    return { table: c.table, colonneMatricule: c.colonneMatricule, variables: c.variables };
  }

  private appliquer(c: PayrollWriteTargets): void {
    this.config.set(c);
    this.brouillon.set({ table: c.table, colonneMatricule: c.colonneMatricule, variables: c.variables.map((v) => ({ ...v })) });
    this.colonnes.set(c.colonnes);
    this.loading.set(false);
    this.erreurs.set([]);
  }

  private maj(f: (d: DestinationsBrouillon) => DestinationsBrouillon): void {
    this.brouillon.update((d) => (d ? f(d) : d));
    this.erreurs.set([]);
  }
}
