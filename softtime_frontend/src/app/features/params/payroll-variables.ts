import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { errorMessage } from '../../core/error.logic';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';
import { ToastService } from '../../core/toast.service';
import { PayrollVariable, PayrollVariablesConfig, SageConstantOption } from '../../shared/models';
import { PayrollVariablesService } from '../../shared/services/payroll-variables.service';
import { SoftBadge, SoftButton, SoftCard } from '../../shared/components';
import { PayrollDestinations } from './payroll-destinations';
import {
  aCodePropre,
  changementOption,
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
  imports: [SoftBadge, SoftButton, SoftCard, PayrollDestinations],
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
        <p class="pv-banner pv-banner--info" role="status">Vous n'avez pas la permission de modifier la base {{ base() }} : configuration non disponible.</p>
      } @else if (loading()) {
        <p class="pv-note">Chargement…</p>
      } @else if (config(); as c) {
        <p class="pv-note">
          Le calcul ne change pas : les neuf variables restent calculées et affichées. Seul l'envoi vers la paie est filtré ;
          une variable décochée n'est pas écrite (la valeur déjà présente en paie n'est pas modifiée).
          Le changement s'applique au prochain envoi.
        </p>
        @if (c.parDefaut) {
          <p class="pv-banner pv-banner--info" role="status">Aucune configuration enregistrée : comportement d'origine (7 variables d'heures supplémentaires envoyées).</p>
        }
        @if (c.moisEnvoye) {
          <p class="pv-banner pv-banner--warning" role="status">
            Mois déjà envoyé en paie (période {{ c.dernierEnvoi }}) : modification impossible. La configuration pourra être
            modifiée le mois prochain.
          </p>
        }
        @if (erreurs().length) {
          <div class="pv-banner pv-banner--danger" role="alert">
            @for (e of erreurs(); track $index) { <p>{{ e }}</p> }
          </div>
        }

        <div class="soft-table-wrap pv-wrap">
          <table class="soft-table pv-table">
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
                  <td class="pv-name">
                    {{ v.libelle }}
                    @if (!v.actif) { <soft-badge variant="muted">{{ nonEnvoyee }}</soft-badge> }
                  </td>
                  <td data-label="Envoyée">
                    <label class="soft-toggle pv-toggle">
                      <input type="checkbox" [attr.aria-label]="'Envoyer ' + v.libelle + ' à la paie'"
                        [checked]="v.actif" [disabled]="!cochable(v) || saving()"
                        (change)="basculer(v.variable, $any($event.target).checked)" />
                      <span class="soft-toggle__track"><span class="soft-toggle__thumb"></span></span>
                    </label>
                    @if (raison(v); as r) { <div class="soft-field__hint pv-hint">{{ r }}</div> }
                  </td>
                  <td [attr.data-label]="autre() ? 'Colonne cible' : 'Code de paie'">
                    @if (codePropre(v.variable) && !sansEnvoi(v.variable)) {
                      @if (autre()) {
                        <span class="pv-readonly" title="Choisie dans « Destinations dans la base de paie »">
                          {{ v.cibleExistante ?? '(à choisir dans les destinations)' }}
                        </span>
                      } @else {
                        <div class="soft-select pv-field">
                          <div class="soft-select__control">
                            <select [attr.aria-label]="'Code de paie de ' + v.libelle" [disabled]="c.moisEnvoye || saving()"
                              (change)="changer(v.variable, 'codePaie', $any($event.target).value)">
                              <option value="" [selected]="!v.codePaie">(aucun)</option>
                              @for (o of codes(v); track o.code) {
                                <option [value]="o.code" [selected]="o.code.toLowerCase() === (v.codePaie ?? '').toLowerCase()">{{ o.code }}{{ o.intitule ? ' — ' + o.intitule : '' }}</option>
                              }
                            </select>
                          </div>
                        </div>
                        @if (c.avertissementCodes) { <div class="soft-field__hint pv-hint">{{ c.avertissementCodes }}</div> }
                      }
                    } @else if (sansEnvoi(v.variable)) {
                      @if (autre()) {
                        <div class="soft-select pv-field">
                          <div class="soft-select__control">
                            <select [attr.aria-label]="'Colonne cible de ' + v.libelle" [disabled]="c.moisEnvoye || saving()"
                              (change)="changer(v.variable, 'colonneCible', $any($event.target).value)">
                              <option value="" [selected]="!v.colonneCible">(aucune)</option>
                              @for (col of colonnes(v); track col) { <option [value]="col" [selected]="col === v.colonneCible">{{ col }}</option> }
                            </select>
                          </div>
                        </div>
                      } @else {
                        <input type="text" class="soft-input pv-field" maxlength="32" [attr.aria-label]="'Code de paie de ' + v.libelle"
                          [disabled]="c.moisEnvoye || saving()" [value]="v.codePaie ?? ''"
                          (input)="changer(v.variable, 'codePaie', $any($event.target).value)" />
                      }
                    } @else {
                      <span class="pv-readonly" title="Configuré dans {{ autre() ? 'l’écriture de paie de la base' : 'Codes constantes' }}">
                        {{ v.cibleExistante ?? '(non configuré)' }}
                      </span>
                    }
                  </td>
                  <td data-label="Unité">
                    @if (sansEnvoi(v.variable)) {
                      <div class="soft-select pv-field pv-field--unit">
                        <div class="soft-select__control">
                          <select [attr.aria-label]="'Unité de ' + v.libelle" [disabled]="c.moisEnvoye || saving()"
                            (change)="changer(v.variable, 'unite', $any($event.target).value)">
                            <option value="" [selected]="!v.unite">(aucune)</option>
                            <option value="H" [selected]="v.unite === 'H'">heures</option>
                            <option value="J" [selected]="v.unite === 'J'">jours</option>
                          </select>
                        </div>
                      </div>
                    } @else {
                      <span class="pv-readonly">heures</span>
                    }
                  </td>
                </tr>
              }
            </tbody>
          </table>
        </div>
        <div class="pv-option" [class.pv-off]="!remiseAZero()">
          <label class="pv-option__row">
            <span class="soft-toggle pv-toggle">
              <input type="checkbox" [checked]="remiseAZero()" [disabled]="c.moisEnvoye || saving()"
                (change)="basculerRemiseAZero($any($event.target).checked)" />
              <span class="soft-toggle__track"><span class="soft-toggle__thumb"></span></span>
            </span>
            <span class="pv-option__label">Remettre à 0 les salariés sans valeur à l'envoi</span>
          </label>
          <div class="soft-field__hint pv-option__hint">
            Évite qu'un ancien cumul reste dans la paie quand un salarié n'a plus d'heures. À décocher si quelqu'un saisit ces codes à la main dans la paie.
          </div>
          @if (c.moisEnvoye) { <div class="soft-field__hint pv-option__hint">Mois déjà envoyé : modification impossible.</div> }
        </div>
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
        <div class="pv-banner pv-banner--danger" role="alert">
          @for (e of erreurs(); track $index) { <p>{{ e }}</p> }
        </div>
      }
    </soft-card>
  `,
  styles: [`
    :host { display: block; margin-top: var(--space-lg); }
    .pv-head { display: flex; justify-content: space-between; align-items: flex-start; gap: var(--space-md); flex-wrap: wrap;
      margin-bottom: var(--space-md); }
    .pv-title { margin: 0; font-family: var(--font-heading); font-size: 16px; font-weight: 600; color: var(--app-text-strong); }
    .pv-sub { margin: var(--space-xs) 0 0; color: var(--text-secondary); font-size: 14px; }
    .pv-actions { display: flex; gap: var(--space-sm); flex-wrap: wrap; }
    .pv-note { color: var(--text-muted); margin: var(--space-md) 0; font-size: 13px; }
    .pv-banner { margin: var(--space-md) 0; padding: 10px 14px; font-size: 13px; border-radius: var(--radius-md); border: 1px solid; }
    .pv-banner p { margin: 2px 0; }
    .pv-banner--danger { color: var(--danger); background: var(--danger-bg); border-color: var(--danger-border); }
    .pv-banner--warning { color: var(--warning); background: var(--warning-bg); border-color: var(--warning-border); }
    .pv-banner--info { color: var(--info); background: var(--info-bg); border-color: var(--info-border); }
    .pv-table td { vertical-align: top; }
    .pv-off .pv-name { color: var(--text-muted); }
    .pv-name soft-badge { margin-left: var(--space-sm); }
    .pv-hint { margin-top: var(--space-xs); }
    .pv-readonly { color: var(--text-muted); line-height: 44px; }
    .pv-field { min-width: 180px; max-width: 280px; }
    .pv-field--unit { min-width: 140px; max-width: 180px; }
    .pv-table select:disabled { background: var(--surface-dark); color: var(--text-muted); cursor: not-allowed; }
    .pv-toggle { margin-top: 10px; }
    .pv-option { margin-top: var(--space-md); padding: var(--space-md); border: 1px solid var(--color-border); border-radius: var(--radius-lg);
      background: var(--app-surface); }
    .pv-option__row { display: flex; align-items: center; gap: var(--space-md); cursor: pointer; }
    .pv-option__row .pv-toggle { margin-top: 0; flex: 0 0 auto; }
    .pv-option__label { font-weight: 600; color: var(--app-text-strong); font-size: 14px; min-width: 0; overflow-wrap: anywhere; }
    .pv-option__hint { margin: var(--space-xs) 0 0 calc(44px + var(--space-md)); }
    .pv-option:has(input:disabled) .pv-option__row { cursor: not-allowed; }
    .pv-toggle:focus-within .soft-toggle__track { outline: 2px solid var(--focus-ring); outline-offset: 2px; }
    .pv-toggle:has(input:disabled) { cursor: not-allowed; }
    .pv-toggle input:disabled + .soft-toggle__track { opacity: 0.5; }
    /* Largeur étroite : la règle globale masque .soft-table-wrap (les data-table affichent alors leur vue en cartes).
       La carte garde son tableau, présenté en cartes (une par variable, comme .soft-table-card) : mêmes contrôles,
       mêmes états désactivé et verrouillé, mêmes messages d'aide. */
    @media (max-width: 767px) {
      .soft-table-wrap.pv-wrap { display: block; overflow: visible; border: 0; border-radius: 0; box-shadow: none; background: transparent; }
      .pv-table, .pv-table tbody, .pv-table tr, .pv-table td { display: block; }
      .pv-table thead { position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }
      .pv-table tbody { display: grid; gap: 12px; }
      .pv-table tbody tr { display: grid; gap: 10px; padding: 16px; border: 1px solid var(--color-border); border-radius: var(--radius-lg);
        background: var(--app-surface); box-shadow: var(--shadow-xs); }
      .pv-table tbody tr:hover { background: var(--app-surface); }
      .pv-table tbody td { display: grid; grid-template-columns: minmax(0, 42%) minmax(0, 1fr); gap: 4px 12px; align-items: center;
        min-width: 0; padding: 0; border: 0; }
      .pv-table tbody td::before { content: attr(data-label); color: var(--text-secondary); font-size: 12px; font-weight: 600; }
      .pv-table tbody td.pv-name { display: block; font-weight: 600; }
      .pv-table tbody td.pv-name::before { content: none; }
      .pv-table .pv-hint { grid-column: 2; margin-top: 0; }
      /* Listes et champ de code : étiquette au-dessus, contrôle sur toute la largeur (libellés de codes longs). */
      .pv-table tbody td:has(.pv-field) { grid-template-columns: minmax(0, 1fr); }
      .pv-table tbody td:has(.pv-field) .pv-hint { grid-column: 1; }
      .pv-toggle { margin-top: 0; }
      .pv-option__hint { margin-left: 0; }
      .pv-readonly { line-height: 1.5; }
      .pv-field, .pv-field--unit { min-width: 0; max-width: none; width: 100%; }
    }
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
  /** Brouillon de l'option « remettre à 0 les salariés sans valeur à l'envoi » (vraie par défaut). */
  readonly remiseAZero = signal(true);
  /** Erreurs durables : visibles jusqu'à la prochaine modification, annulation ou tentative d'enregistrement. */
  readonly erreurs = signal<string[]>([]);

  readonly base = computed(() => this.session.activeSageDb());
  readonly autre = computed(() => this.config()?.typeBase === 'AUTRE');
  readonly modifie = computed(() => {
    const c = this.config();
    return !!c && (changements(c.variables, this.brouillon()).length > 0 || this.remiseAZero() !== (c.remiseAZeroSansValeur ?? true));
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
    this.remiseAZero.set(c.remiseAZeroSansValeur ?? true);
    this.erreurs.set([]);
  }

  basculerRemiseAZero(actif: boolean): void {
    this.remiseAZero.set(actif);
    this.erreurs.set([]);
  }

  async enregistrer(): Promise<void> {
    const c = this.config();
    if (!c || this.saving() || c.moisEnvoye) return;
    const erreurs = validerBrouillon(this.brouillon(), this.autre());
    this.erreurs.set(erreurs);
    if (erreurs.length) return;
    const option = changementOption(c.remiseAZeroSansValeur ?? true, this.remiseAZero());
    const liste = [...changements(c.variables, this.brouillon()), ...(option ? [option] : [])];
    if (!liste.length) return;
    const question = `Enregistrer pour la base ${c.base} : ${liste.join(' ; ')} ? Le changement s'appliquera au prochain envoi en paie.`;
    if (!(await this.confirm.ask(question, 'Variables envoyées à la paie', 'Enregistrer'))) return;
    this.saving.set(true);
    this.svc.save(versEnregistrement(this.brouillon()), this.remiseAZero()).subscribe({
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
    this.remiseAZero.set(c.remiseAZeroSansValeur ?? true);
    this.loading.set(false);
    this.erreurs.set([]);
  }

  private maj(variable: string, f: (v: PayrollVariable) => PayrollVariable): void {
    this.brouillon.update((liste) => liste.map((v) => (v.variable === variable ? f(v) : v)));
    this.erreurs.set([]);
  }
}
