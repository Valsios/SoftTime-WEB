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
          <p class="pd-alert" role="status">
            Mois déjà envoyé en paie (période {{ config()!.dernierEnvoi }}) : modification impossible.
          </p>
        }
        @if (erreurs().length) {
          <div class="pd-errors" role="alert">
            @for (e of erreurs(); track $index) { <p>{{ e }}</p> }
          </div>
        }
        @if (!config()!.tables.length) {
          <p class="pd-note">La liste des tables de la base de paie n'a pas pu être lue.</p>
        }
        <div class="pd-fields">
          <label>
            Table
            <select aria-label="Table de destination" [disabled]="bloque()" [value]="d.table ?? ''"
              (change)="choisirTable($any($event.target).value)">
              <option value="">(aucune)</option>
              @for (t of tables(); track t) { <option [value]="t">{{ t }}</option> }
            </select>
          </label>
          <label>
            Colonne matricule
            <select aria-label="Colonne matricule" [disabled]="bloque() || !d.table" [value]="d.colonneMatricule ?? ''"
              (change)="choisirMatricule($any($event.target).value)">
              <option value="">(aucune)</option>
              @for (c of colonnesMatricule(); track c) { <option [value]="c">{{ c }}</option> }
            </select>
          </label>
        </div>
        <table class="pd-table">
          <thead>
            <tr><th>Variable</th><th>Envoyée</th><th>Colonne</th></tr>
          </thead>
          <tbody>
            @for (v of d.variables; track v.variable) {
              <tr>
                <td>{{ v.libelle }}</td>
                <td>{{ v.actif ? 'oui' : 'non' }}</td>
                <td>
                  <select [attr.aria-label]="'Colonne de ' + v.libelle" [disabled]="bloque() || !d.table" [value]="v.colonne ?? ''"
                    (change)="choisirColonne(v.variable, $any($event.target).value)">
                    <option value="">(aucune)</option>
                    @for (c of choix(v.decimalesRequises, v.colonne); track c.nom) {
                      <option [value]="c.nom" [disabled]="!c.utilisable">{{ c.libelle }}</option>
                    }
                  </select>
                </td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>
  `,
  styles: [`
    .pd { margin-top: 20px; padding-top: 12px; border-top: 1px solid var(--border-subtle); }
    .pd-head { display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; flex-wrap: wrap; }
    .pd-title { font-size: 15px; margin: 0; }
    .pd-actions { display: flex; gap: 8px; flex-wrap: wrap; }
    .pd-note { color: var(--text-muted); margin: 10px 0; font-size: 13px; }
    .pd-alert, .pd-errors { border: 1px solid var(--danger-border); border-left: 4px solid var(--danger);
      background: var(--danger-bg); padding: 8px 12px; margin: 10px 0; border-radius: 6px; }
    .pd-errors p { margin: 2px 0; }
    .pd-fields { display: flex; gap: 16px; flex-wrap: wrap; margin: 8px 0; }
    .pd-fields label { display: flex; flex-direction: column; gap: 4px; font-size: 13px; }
    .pd-table { width: 100%; border-collapse: collapse; margin-top: 8px; }
    .pd-table th, .pd-table td { text-align: left; padding: 8px; border-bottom: 1px solid var(--border-subtle); }
    .pd select { max-width: 260px; width: 100%; }
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
