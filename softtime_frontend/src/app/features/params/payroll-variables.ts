import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { errorMessage } from '../../core/error.logic';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';
import { ToastService } from '../../core/toast.service';
import { PayrollVariable, PayrollVariablesConfig, SageConstantOption } from '../../shared/models';
import { PayrollVariablesService } from '../../shared/services/payroll-variables.service';
import { SoftButton, SoftCard } from '../../shared/components';
import { PayrollDestinations } from './payroll-destinations';
import {
  aCodePropre,
  changements,
  estSansEnvoi,
  NON_ENVOYEE,
  peutCocher,
  raisonNonCochable,
  validerBrouillon,
  versEnregistrement,
} from './payroll-variables.logic';

/**
 * « Variables envoyées à la paie » de la base SAGE active. Rien n'est envoyé au serveur avant la confirmation ; les
 * erreurs restent affichées jusqu'à la prochaine action. Mêmes droits que la modification de la base.
 */
@Component({
  selector: 'app-payroll-variables',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SoftButton, SoftCard, PayrollDestinations],
  template: `
    <soft-card>
      <div class="pv-head">
        <div>
          <h2 class="pv-title">Variables envoyées à la paie</h2>
          <p class="pv-sub">
            Base active : <strong>{{ base() ?? 'aucune' }}</strong>
            @if (config(); as c) { — {{ c.typeBase === 'AUTRE' ? 'autre base' : 'base SAGE' }} }
          </p>
        </div>
        @if (config() && !config()!.moisEnvoye) {
          <div class="pv-actions">
            <soft-button variant="ghost" [disabled]="!modifie() || saving()" (click)="annuler()">Annuler les changements</soft-button>
            <soft-button [loading]="saving()" [disabled]="!modifie()" (click)="enregistrer()">Enregistrer</soft-button>
          </div>
        }
      </div>

      @if (!base()) {
        <p class="pv-note">Choisissez une base active dans la barre du haut pour configurer ses variables de paie.</p>
      } @else if (!autorise()) {
        <p class="pv-note">Vous n'avez pas la permission de modifier la base {{ base() }} : configuration non disponible.</p>
      } @else if (loading()) {
        <p class="pv-note">Chargement…</p>
      } @else if (config(); as c) {
        <p class="pv-note">
          Le calcul ne change pas : les neuf variables restent calculées et affichées. Seul l'envoi vers la paie est filtré ;
          une variable décochée n'est pas écrite (la valeur déjà présente en paie n'est pas modifiée).
          Le changement s'applique au prochain envoi.
        </p>
        @if (c.parDefaut) {
          <p class="pv-note">Aucune configuration enregistrée : comportement d'origine (7 variables d'heures supplémentaires envoyées).</p>
        }
        @if (c.moisEnvoye) {
          <p class="pv-alert" role="status">
            Mois déjà envoyé en paie (période {{ c.dernierEnvoi }}) : modification impossible. La configuration pourra être
            modifiée le mois prochain.
          </p>
        }
        @if (erreurs().length) {
          <div class="pv-errors" role="alert">
            @for (e of erreurs(); track $index) { <p>{{ e }}</p> }
          </div>
        }

        <table class="pv-table">
          <thead>
            <tr>
              <th>Variable</th>
              <th>Envoyée</th>
              <th>{{ autre() ? 'Colonne cible' : 'Code de paie' }}</th>
              <th>Unité</th>
            </tr>
          </thead>
          <tbody>
            @for (v of brouillon(); track v.variable) {
              <tr [class.pv-off]="!v.actif">
                <td>
                  {{ v.libelle }}
                  @if (!v.actif) { <span class="pv-tag">{{ nonEnvoyee }}</span> }
                </td>
                <td>
                  <input type="checkbox" [attr.aria-label]="'Envoyer ' + v.libelle + ' à la paie'"
                    [checked]="v.actif" [disabled]="!cochable(v) || saving()"
                    (change)="basculer(v.variable, $any($event.target).checked)" />
                  @if (raison(v); as r) { <div class="pv-hint">{{ r }}</div> }
                </td>
                <td>
                  @if (codePropre(v.variable) && !sansEnvoi(v.variable)) {
                    @if (autre()) {
                      <span class="pv-readonly" title="Choisie dans « Destinations dans la base de paie »">
                        {{ v.cibleExistante ?? '(à choisir dans les destinations)' }}
                      </span>
                    } @else {
                      <select [attr.aria-label]="'Code de paie de ' + v.libelle" [disabled]="c.moisEnvoye || saving()"
                        [value]="v.codePaie ?? ''" (change)="changer(v.variable, 'codePaie', $any($event.target).value)">
                        <option value="">(aucun)</option>
                        @for (o of codes(v); track o.code) {
                          <option [value]="o.code">{{ o.code }}{{ o.intitule ? ' — ' + o.intitule : '' }}</option>
                        }
                      </select>
                      @if (c.avertissementCodes) { <div class="pv-hint">{{ c.avertissementCodes }}</div> }
                    }
                  } @else if (sansEnvoi(v.variable)) {
                    @if (autre()) {
                      <select [attr.aria-label]="'Colonne cible de ' + v.libelle" [disabled]="c.moisEnvoye || saving()"
                        [value]="v.colonneCible ?? ''" (change)="changer(v.variable, 'colonneCible', $any($event.target).value)">
                        <option value="">(aucune)</option>
                        @for (col of colonnes(v); track col) { <option [value]="col">{{ col }}</option> }
                      </select>
                    } @else {
                      <input type="text" maxlength="32" [attr.aria-label]="'Code de paie de ' + v.libelle"
                        [disabled]="c.moisEnvoye || saving()" [value]="v.codePaie ?? ''"
                        (input)="changer(v.variable, 'codePaie', $any($event.target).value)" />
                    }
                  } @else {
                    <span class="pv-readonly" title="Configuré dans {{ autre() ? 'l’écriture de paie de la base' : 'Codes constantes' }}">
                      {{ v.cibleExistante ?? '(non configuré)' }}
                    </span>
                  }
                </td>
                <td>
                  @if (sansEnvoi(v.variable)) {
                    <select [attr.aria-label]="'Unité de ' + v.libelle" [disabled]="c.moisEnvoye || saving()"
                      [value]="v.unite ?? ''" (change)="changer(v.variable, 'unite', $any($event.target).value)">
                      <option value="">(aucune)</option>
                      <option value="H">heures</option>
                      <option value="J">jours</option>
                    </select>
                  } @else {
                    <span class="pv-readonly">heures</span>
                  }
                </td>
              </tr>
            }
          </tbody>
        </table>
        <p class="pv-note">
          Retard est envoyé en heures, avec la valeur affichée dans la page Heures supplémentaires
          ({{ autre() ? 'dans sa colonne de destination' : 'dans le code de paie choisi' }}).
          Absence : l'envoi vers la paie n'existe pas encore (prochaine étape) ; elle ne peut pas être cochée et n'est
          jamais écrite.
        </p>
        @if (autre()) {
          <app-payroll-destinations (enregistre)="charger()" />
        }
      } @else if (erreurs().length) {
        <div class="pv-errors" role="alert">
          @for (e of erreurs(); track $index) { <p>{{ e }}</p> }
        </div>
      }
    </soft-card>
  `,
  styles: [`
    :host { display: block; margin-top: 16px; }
    .pv-head { display: flex; justify-content: space-between; align-items: flex-start; gap: 12px; flex-wrap: wrap; }
    .pv-title { font-size: 16px; margin: 0; }
    .pv-sub { margin: 4px 0 0; color: var(--text-muted); }
    .pv-actions { display: flex; gap: 8px; flex-wrap: wrap; }
    .pv-note { color: var(--text-muted); margin: 10px 0; font-size: 13px; }
    .pv-alert, .pv-errors { border: 1px solid var(--danger-border); border-left: 4px solid var(--danger);
      background: var(--danger-bg); padding: 8px 12px; margin: 10px 0; border-radius: 6px; }
    .pv-errors p { margin: 2px 0; }
    .pv-table { width: 100%; border-collapse: collapse; margin-top: 8px; }
    .pv-table th, .pv-table td { text-align: left; padding: 8px; border-bottom: 1px solid var(--border-subtle); vertical-align: top; }
    .pv-off td:first-child { color: var(--text-muted); }
    .pv-tag { margin-left: 6px; font-size: 12px; color: var(--text-muted); font-style: italic; }
    .pv-hint { font-size: 12px; color: var(--text-muted); margin-top: 2px; }
    .pv-readonly { color: var(--text-muted); }
    .pv-table input[type='text'], .pv-table select { max-width: 220px; width: 100%; }
  `],
})
export class PayrollVariablesCard implements OnInit {
  private readonly svc = inject(PayrollVariablesService);
  private readonly session = inject(SessionStore);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  protected readonly nonEnvoyee = NON_ENVOYEE;
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly config = signal<PayrollVariablesConfig | null>(null);
  readonly brouillon = signal<PayrollVariable[]>([]);
  /** Erreurs durables : visibles jusqu'à la prochaine modification, annulation ou tentative d'enregistrement. */
  readonly erreurs = signal<string[]>([]);

  readonly base = computed(() => this.session.activeSageDb());
  readonly autre = computed(() => this.config()?.typeBase === 'AUTRE');
  readonly modifie = computed(() => {
    const c = this.config();
    return !!c && changements(c.variables, this.brouillon()).length > 0;
  });

  /** Même règle que le bouton « Modifier » de la base active (le serveur vérifie de nouveau). */
  readonly autorise = computed(() => {
    const base = this.base();
    if (!base) return false;
    const perms = this.session.permissions();
    if (!perms || perms.length === 0) return this.session.hasRight(Droit.Databases);
    return perms.includes(`SAGE:${base}:UPDATE`);
  });

  ngOnInit(): void {
    this.charger();
  }

  charger(): void {
    if (!this.base() || !this.autorise()) return;
    this.loading.set(true);
    this.svc.get().subscribe({
      next: (c) => this.appliquer(c),
      error: (e) => {
        this.loading.set(false);
        this.erreurs.set([errorMessage(e)]);
      },
    });
  }

  sansEnvoi(variable: string): boolean {
    return estSansEnvoi(variable);
  }

  codePropre(variable: string): boolean {
    return aCodePropre(variable);
  }

  /** Codes lus dans SAGE ; le code déjà enregistré reste visible même s'il n'y figure plus. */
  codes(v: PayrollVariable): SageConstantOption[] {
    const liste = this.config()?.codesPaie ?? [];
    return v.codePaie && !liste.some((o) => o.code.toLowerCase() === v.codePaie!.toLowerCase())
      ? [{ code: v.codePaie, intitule: 'introuvable dans SAGE' }, ...liste]
      : liste;
  }

  cochable(v: PayrollVariable): boolean {
    return peutCocher(v, this.autre(), this.config()?.moisEnvoye ?? true);
  }

  raison(v: PayrollVariable): string | null {
    return raisonNonCochable(v, this.autre(), this.config()?.moisEnvoye ?? false);
  }

  /** Colonnes proposées ; la valeur déjà enregistrée reste visible même si la table ne la contient plus. */
  colonnes(v: PayrollVariable): string[] {
    const liste = this.config()?.colonnesCibles ?? [];
    return v.colonneCible && !liste.includes(v.colonneCible) ? [v.colonneCible, ...liste] : liste;
  }

  basculer(variable: string, actif: boolean): void {
    this.maj(variable, (v) => ({ ...v, actif }));
  }

  changer(variable: string, champ: 'codePaie' | 'colonneCible' | 'unite', valeur: string): void {
    this.maj(variable, (v) => {
      const suivant = { ...v, [champ]: valeur.trim() ? valeur : null };
      // Cible effacée : la variable ne peut plus rester cochée.
      return champ !== 'unite' && !suivant[champ] ? { ...suivant, actif: false } : suivant;
    });
  }

  annuler(): void {
    const c = this.config();
    if (!c) return;
    this.brouillon.set(c.variables.map((v) => ({ ...v })));
    this.erreurs.set([]);
  }

  async enregistrer(): Promise<void> {
    const c = this.config();
    if (!c || this.saving() || c.moisEnvoye) return;
    const erreurs = validerBrouillon(this.brouillon(), this.autre());
    this.erreurs.set(erreurs);
    if (erreurs.length) return;
    const liste = changements(c.variables, this.brouillon());
    if (!liste.length) return;
    const question = `Enregistrer pour la base ${c.base} : ${liste.join(' ; ')} ? Le changement s'appliquera au prochain envoi en paie.`;
    if (!(await this.confirm.ask(question, 'Variables envoyées à la paie', 'Enregistrer'))) return;
    this.saving.set(true);
    this.svc.save(versEnregistrement(this.brouillon())).subscribe({
      next: (resultat) => {
        this.saving.set(false);
        this.appliquer(resultat);
        this.toast.success('Variables envoyées à la paie enregistrées.');
      },
      error: (e) => {
        this.saving.set(false);
        this.erreurs.set([errorMessage(e)]);
      },
    });
  }

  private appliquer(c: PayrollVariablesConfig): void {
    this.config.set(c);
    this.brouillon.set(c.variables.map((v) => ({ ...v })));
    this.loading.set(false);
    this.erreurs.set([]);
  }

  private maj(variable: string, f: (v: PayrollVariable) => PayrollVariable): void {
    this.brouillon.update((liste) => liste.map((v) => (v.variable === variable ? f(v) : v)));
    this.erreurs.set([]);
  }
}
