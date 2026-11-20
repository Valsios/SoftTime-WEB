import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ConfirmService } from '../../core/confirm.service';
import { errorMessage } from '../../core/error.logic';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';
import { ToastService } from '../../core/toast.service';
import { PayrollAbsenceCode, PayrollVariable, PayrollVariablesConfig, SageConstantOption } from '../../shared/models';
import { PayrollVariablesService } from '../../shared/services/payroll-variables.service';
import { SoftBadge, SoftButton, SoftCard, SoftSelect } from '../../shared/components';
import { SelectOption } from '../../shared/components/soft-select';
import { PayrollDestinations } from './payroll-destinations';
import { PayrollDomainsSetting } from './payroll-domains';
import {
  aCodePropre,
  AIDE_CODES_ABSENCE,
  AIDE_HEURES_PAR_JOUR,
  aUniteAuChoix,
  changementHeuresParJour,
  changementOption,
  changements,
  changementsCodesAbsence,
  cleCode,
  CodesAbsenceBrouillon,
  codesAbsenceLus,
  estSansEnvoi,
  HEURES_PAR_JOUR_DEFAUT,
  NON_ENVOYEE,
  peutCocher,
  raisonNonCochable,
  TITRE_CODES_ABSENCE,
  validerBrouillon,
  versEnregistrement,
} from './payroll-variables.logic';

/** Variables dont la liste des codes propose la recherche en tapant (lot C3 : Absence). */
const CODES_AVEC_RECHERCHE: readonly string[] = ['Absence'];
/** Au-delà de ce nombre de caractères, le libellé d'un code est répété en entier sous le champ sur écran étroit. */
const LIBELLE_CODE_LONG = 24;

/**
 * « Variables envoyées à la paie » de la base SAGE active. Rien n'est envoyé au serveur avant la confirmation ; les
 * erreurs restent affichées jusqu'à la prochaine action. Mêmes droits que la modification de la base.
 */
@Component({
  selector: 'app-payroll-variables',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, SoftBadge, SoftButton, SoftCard, SoftSelect, PayrollDestinations, PayrollDomainsSetting],
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
          Cocher ou décocher une variable ne change pas le calcul : les neuf variables restent calculées et affichées. Seul
          l'envoi vers la paie est filtré ; une variable décochée n'est pas écrite (la valeur déjà présente en paie n'est pas
          modifiée). Le changement s'applique au prochain envoi. Seuls les codes d'absence comptés changent le calcul de
          l'Absence, au prochain calcul.
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
                    <span class="pv-name__text">{{ v.libelle }}</span>
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
                      } @else if (avecRecherche(v.variable)) {
                        <!-- Absence : liste des codes lus dans SAGE, avec recherche en tapant (aucune saisie libre). -->
                        <soft-select class="pv-field pv-field--search" [attr.title]="titreCode(v)" [ariaLabel]="'Code de paie de ' + v.libelle" placeholder="(aucun)"
                          filterPlaceholder="Rechercher un code…" [filterable]="true" [options]="optionsCodes(v)"
                          [ngModel]="v.codePaie" [ngModelOptions]="{ standalone: true }" [disabled]="c.moisEnvoye || saving()"
                          (ngModelChange)="changer(v.variable, 'codePaie', $event ?? '')" />
                        @if (libelleLong(v); as t) { <div class="soft-field__hint pv-hint pv-codefull">{{ t }}</div> }
                        @if (c.avertissementCodes) { <div class="soft-field__hint pv-hint">{{ c.avertissementCodes }}</div> }
                      } @else {
                        <div class="soft-select pv-field">
                          <div class="soft-select__control">
                            <select [attr.aria-label]="'Code de paie de ' + v.libelle" [attr.title]="titreCode(v)" [disabled]="c.moisEnvoye || saving()"
                              (change)="changer(v.variable, 'codePaie', $any($event.target).value)">
                              <option value="" [selected]="!v.codePaie">(aucun)</option>
                              @for (o of codes(v); track o.code) {
                                <option [value]="o.code" [selected]="o.code.toLowerCase() === (v.codePaie ?? '').toLowerCase()">{{ o.code }}{{ o.intitule ? ' — ' + o.intitule : '' }}</option>
                              }
                            </select>
                          </div>
                        </div>
                        @if (libelleLong(v); as t) { <div class="soft-field__hint pv-hint pv-codefull">{{ t }}</div> }
                        @if (c.avertissementCodes) { <div class="soft-field__hint pv-hint">{{ c.avertissementCodes }}</div> }
                      }
                    } @else {
                      <span class="pv-readonly" title="Configuré dans {{ autre() ? 'l’écriture de paie de la base' : 'Codes constantes' }}">
                        {{ v.cibleExistante ?? '(non configuré)' }}
                      </span>
                    }
                  </td>
                  <td data-label="Unité">
                    @if (uniteAuChoix(v.variable)) {
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
                      @if (v.unite === 'J') {
                        <label class="pv-hpj">
                          <span class="pv-hpj__label">Heures par jour</span>
                          <input type="number" class="soft-input pv-hpj__input" min="1" max="24" step="0.5" inputmode="decimal"
                            aria-label="Heures par jour de l'absence" [disabled]="c.moisEnvoye || saving()"
                            [value]="heuresParJour() ?? ''" (input)="changerHeuresParJour($any($event.target).value)" />
                        </label>
                        <div class="soft-field__hint pv-hint">{{ aideHeuresParJour }}</div>
                      }
                    } @else {
                      <span class="pv-readonly">heures</span>
                    }
                  </td>
                </tr>
                @if (v.variable === 'Absence') {
                  <tr class="pv-codes-row" [class.pv-off]="!v.actif">
                    <td colspan="4" class="pv-codes-cell">
                      <fieldset class="pv-codes" [disabled]="c.moisEnvoye || saving()">
                        <legend class="pv-codes__title">{{ titreCodes }}</legend>
                        <label class="pv-code">
                          <input type="checkbox" [checked]="codesAbsence().sansCode"
                            (change)="basculerSansCode($any($event.target).checked)" />
                          <span class="pv-code__label">Arbitrage d'absence sans code</span>
                        </label>
                        @for (code of listeCodesAbsence(); track code.libelle) {
                          <label class="pv-code" [class.pv-code--missing]="code.introuvable">
                            <input type="checkbox" [checked]="estCompte(code.libelle)"
                              (change)="basculerCode(code.libelle, $any($event.target).checked)" />
                            <span class="pv-code__label">{{ code.libelle }}</span>
                            @if (code.introuvable) { <soft-badge variant="warning">code introuvable</soft-badge> }
                            @if (code.nonPaye) { <soft-badge variant="muted">non payé</soft-badge> }
                            @if (code.suggere && !estCompte(code.libelle)) { <soft-badge variant="info">à envisager</soft-badge> }
                          </label>
                        }
                      </fieldset>
                      @if (aCodeIntrouvable()) {
                        <div class="soft-field__hint pv-codes__warn" role="status">
                          Un code enregistré n'existe plus dans les codes d'absence : il reste compté tant qu'il n'est pas décoché.
                        </div>
                      }
                      <div class="soft-field__hint pv-codes__hint">{{ aideCodes }}</div>
                    </td>
                  </tr>
                }
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
        <app-payroll-domains [config]="c" (enregistre)="charger()" />
        <p class="pv-note">
          Retard est envoyé en heures et Absence en heures ou en jours, avec la valeur affichée dans la page Heures
          supplémentaires ({{ autre() ? 'dans leur colonne de destination' : 'dans le code de paie choisi' }}) ; en jours,
          l'absence est divisée par les heures par jour et arrondie au centième. L'Absence ne compte que les absences non
          justifiées : jours planifiés sans aucune ligne et absences arbitrées dont le code est coché ci-dessus.
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
    /* Axe commun : chaque cellule commence par une ligne de 44 px (hauteur d'une liste, d'un champ ou d'un interrupteur
       avec sa marge) dont le contenu est centré ; l'aide, la pastille ou le champ « Heures par jour » viennent dessous,
       sans décaler le nom, l'interrupteur, le code et l'unité les uns par rapport aux autres. */
    .pv-table td { vertical-align: top; }
    .pv-off .pv-name { color: var(--text-muted); }
    .pv-name__text { display: block; line-height: 44px; }
    .pv-name soft-badge { display: block; width: fit-content; margin: -6px 0 0; }
    .pv-hint { margin-top: var(--space-xs); }
    .pv-codefull { display: none; overflow-wrap: anywhere; }
    .pv-readonly { color: var(--text-muted); line-height: 44px; }
    /* Libellés de codes longs (« ABS01 — Essai Absence », « HS-FERIE — … ») : champ plus large ; le libellé complet est aussi
       dans l'infobulle (title) quand il dépasse encore. */
    .pv-field { min-width: 220px; max-width: 440px; }
    .pv-field--unit { min-width: 140px; max-width: 180px; }
    .pv-table select:disabled { background: var(--surface-dark); color: var(--text-muted); cursor: not-allowed; }
    .pv-toggle { margin-top: 10px; }
    .pv-field--search { display: block; }
    .pv-hpj { display: flex; align-items: center; gap: var(--space-sm); margin-top: var(--space-sm); }
    .pv-hpj__label { font-size: 13px; color: var(--text-secondary); white-space: nowrap; }
    .pv-hpj__input { width: 96px; }
    .pv-hpj__input:disabled { background: var(--surface-dark); color: var(--text-muted); cursor: not-allowed; }
    .pv-codes-row > td { border-top: 0; padding-top: 0; }
    .pv-codes { margin: 0; padding: var(--space-md); border: 1px solid var(--color-border); border-radius: var(--radius-lg);
      background: var(--app-surface); display: grid; grid-template-columns: repeat(auto-fill, minmax(240px, 1fr)); gap: var(--space-sm) var(--space-md); }
    .pv-codes__title { padding: 0 var(--space-xs); font-weight: 600; font-size: 14px; color: var(--app-text-strong); }
    .pv-code { display: flex; align-items: center; flex-wrap: wrap; gap: var(--space-xs) var(--space-sm); min-width: 0; cursor: pointer;
      font-size: 13px; color: var(--app-text); }
    .pv-code input { width: 18px; height: 18px; margin: 0; flex: 0 0 auto; accent-color: var(--accent); cursor: pointer; }
    .pv-code__label { min-width: 0; overflow-wrap: anywhere; }
    .pv-code--missing .pv-code__label { color: var(--warning); }
    .pv-codes:disabled .pv-code, .pv-codes:disabled .pv-code input { cursor: not-allowed; }
    .pv-codes:disabled .pv-code { color: var(--text-muted); }
    .pv-codes__hint, .pv-codes__warn { margin-top: var(--space-xs); }
    .pv-codes__warn { color: var(--warning); }
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
    /* Largeur moyenne (menu latéral affiché, carte d'environ 670 px à 1024 px) : largeurs minimales resserrées pour que le
       tableau tienne dans sa carte sans défilement horizontal ; le champ « Heures par jour » passe à la ligne sous son libellé. */
    @media (min-width: 768px) and (max-width: 1180px) {
      .pv-field { min-width: 190px; }
      .pv-field--unit { min-width: 110px; }
      /* La colonne du code de paie prend toute la largeur libre (libellés « CODE — intitulé »), les trois autres restent à leur
         largeur minimale. */
      .pv-table th:nth-child(1), .pv-table th:nth-child(2), .pv-table th:nth-child(4) { width: 1%; }
      .pv-table th:nth-child(3) { width: 97%; }
      .pv-hpj { flex-wrap: wrap; }
      .pv-hpj__input { width: 84px; }
      /* Dans cette plage la colonne du code reste plus étroite qu'un libellé long : il est répété en entier sous le champ. */
      .pv-codefull { display: block; }
    }
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
      .pv-name__text { display: inline; line-height: inherit; }
      /* Le champ de code fait toute la largeur de la carte mais reste étroit : un libellé long est répété en entier dessous. */
      .pv-codefull { display: block; }
      .pv-name soft-badge { display: inline-block; margin: 0 0 0 var(--space-sm); }
      .pv-table .pv-hint { grid-column: 2; margin-top: 0; }
      /* Listes et champ de code : étiquette au-dessus, contrôle sur toute la largeur (libellés de codes longs). */
      .pv-table tbody td:has(.pv-field) { grid-template-columns: minmax(0, 1fr); }
      .pv-table tbody td:has(.pv-field) .pv-hint { grid-column: 1; }
      .pv-toggle { margin-top: 0; }
      .pv-option__hint { margin-left: 0; }
      .pv-readonly { line-height: 1.5; }
      .pv-field, .pv-field--unit { min-width: 0; max-width: none; width: 100%; }
      /* Codes d'absence comptés : une carte à part, sous la carte de l'Absence, une case par ligne. */
      .pv-table tbody tr.pv-codes-row { padding: 12px; }
      .pv-table tbody td.pv-codes-cell { display: block; }
      .pv-table tbody td.pv-codes-cell::before { content: none; }
      .pv-codes { grid-template-columns: minmax(0, 1fr); padding: var(--space-sm); }
      .pv-table tbody td:has(.pv-hpj) { grid-template-columns: minmax(0, 1fr); }
      .pv-table tbody td:has(.pv-hpj) .pv-hint { grid-column: 1; }
    }
  `],
})
export class PayrollVariablesCard implements OnInit {
  private readonly svc = inject(PayrollVariablesService);
  private readonly session = inject(SessionStore);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  protected readonly nonEnvoyee = NON_ENVOYEE;
  protected readonly aideHeuresParJour = AIDE_HEURES_PAR_JOUR;
  protected readonly aideCodes = AIDE_CODES_ABSENCE;
  protected readonly titreCodes = TITRE_CODES_ABSENCE;
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly config = signal<PayrollVariablesConfig | null>(null);
  readonly brouillon = signal<PayrollVariable[]>([]);
  /** Brouillon de l'option « remettre à 0 les salariés sans valeur à l'envoi » (vraie par défaut). */
  readonly remiseAZero = signal(true);
  /** Brouillon du nombre d'heures par jour de l'Absence en jours (null : champ vidé). */
  readonly heuresParJour = signal<number | null>(HEURES_PAR_JOUR_DEFAUT);
  /** Brouillon des codes d'absence comptés comme non justifiés. */
  readonly codesAbsence = signal<CodesAbsenceBrouillon>({ sansCode: true, codes: [] });
  /** Erreurs durables : visibles jusqu'à la prochaine modification, annulation ou tentative d'enregistrement. */
  readonly erreurs = signal<string[]>([]);

  readonly base = computed(() => this.session.activeSageDb());
  readonly autre = computed(() => this.config()?.typeBase === 'AUTRE');
  readonly modifie = computed(() => {
    const c = this.config();
    return !!c && (changements(c.variables, this.brouillon()).length > 0 || this.remiseAZero() !== (c.remiseAZeroSansValeur ?? true)
      || this.heuresParJour() !== this.heuresLues() || changementsCodesAbsence(codesAbsenceLus(c.absenceCodes), this.codesAbsence()).length > 0);
  });

  /** Codes proposés : ceux lus (T_CODEABSENCE puis codes introuvables). */
  readonly listeCodesAbsence = computed<PayrollAbsenceCode[]>(() => this.config()?.absenceCodes?.codes ?? []);
  readonly aCodeIntrouvable = computed(() => this.listeCodesAbsence().some((c) => c.introuvable && this.estCompte(c.libelle)));

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

  uniteAuChoix(variable: string): boolean {
    return aUniteAuChoix(variable);
  }

  avecRecherche(variable: string): boolean {
    return CODES_AVEC_RECHERCHE.includes(variable);
  }

  /** Options de la liste avec recherche : codes lus dans SAGE (le code enregistré reste visible même s'il n'y figure plus). */
  optionsCodes(v: PayrollVariable): SelectOption[] {
    return this.codes(v).map((o) => ({ value: o.code, label: o.intitule ? `${o.code} — ${o.intitule}` : o.code }));
  }

  /** Libellé complet du code choisi (infobulle : le champ peut le tronquer pour un code très long). */
  titreCode(v: PayrollVariable): string | null {
    const choisi = (v.codePaie ?? '').toLowerCase();
    return choisi ? this.optionsCodes(v).find((o) => String(o.value).toLowerCase() === choisi)?.label ?? v.codePaie : null;
  }

  /** Libellé complet à répéter sous le champ sur écran étroit (infobulle inutilisable au doigt) : seulement s'il est long. */
  libelleLong(v: PayrollVariable): string | null {
    const t = this.titreCode(v);
    return t && t.length > LIBELLE_CODE_LONG ? t : null;
  }

  estCompte(libelle: string): boolean {
    const cle = cleCode(libelle);
    return this.codesAbsence().codes.some((c) => cleCode(c) === cle);
  }

  basculerCode(libelle: string, compte: boolean): void {
    this.codesAbsence.update((b) => {
      const autres = b.codes.filter((c) => cleCode(c) !== cleCode(libelle));
      return { ...b, codes: compte ? [...autres, libelle] : autres };
    });
    this.erreurs.set([]);
  }

  basculerSansCode(compte: boolean): void {
    this.codesAbsence.update((b) => ({ ...b, sansCode: compte }));
    this.erreurs.set([]);
  }

  changerHeuresParJour(valeur: string): void {
    const texte = (valeur ?? '').trim().replace(',', '.');
    const nombre = texte === '' ? null : Number(texte);
    this.heuresParJour.set(nombre === null || Number.isNaN(nombre) ? null : nombre);
    this.erreurs.set([]);
  }

  private heuresLues(): number {
    return this.config()?.heuresParJour ?? HEURES_PAR_JOUR_DEFAUT;
  }

  /** L'Absence est-elle en jours dans le brouillon (le champ « Heures par jour » est alors affiché et contrôlé) ? */
  private absenceEnJours(): boolean {
    return this.brouillon().some((v) => v.variable === 'Absence' && v.unite === 'J');
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
    this.heuresParJour.set(c.heuresParJour ?? HEURES_PAR_JOUR_DEFAUT);
    this.codesAbsence.set(codesAbsenceLus(c.absenceCodes));
    this.erreurs.set([]);
  }

  basculerRemiseAZero(actif: boolean): void {
    this.remiseAZero.set(actif);
    this.erreurs.set([]);
  }

  async enregistrer(): Promise<void> {
    const c = this.config();
    if (!c || this.saving() || c.moisEnvoye) return;
    // Heures par jour contrôlées quand l'Absence est en jours, ou quand le champ a été modifié.
    const heures = this.heuresParJour();
    const heuresModifiees = heures !== this.heuresLues();
    const erreurs = validerBrouillon(this.brouillon(), this.autre(), this.absenceEnJours() || heuresModifiees ? heures : undefined);
    this.erreurs.set(erreurs);
    if (erreurs.length) return;
    const option = changementOption(c.remiseAZeroSansValeur ?? true, this.remiseAZero());
    const lignesHeures = heuresModifiees ? changementHeuresParJour(this.heuresLues(), heures) : null;
    const lignesCodes = changementsCodesAbsence(codesAbsenceLus(c.absenceCodes), this.codesAbsence());
    const liste = [
      ...changements(c.variables, this.brouillon()),
      ...(lignesHeures ? [lignesHeures] : []),
      ...lignesCodes,
      ...(option ? [option] : []),
    ];
    if (!liste.length) return;
    const suite = lignesCodes.length
      ? ' Le changement s\'appliquera au prochain envoi en paie ; les codes d\'absence comptés, au prochain calcul (recalculez les périodes déjà calculées avant de les envoyer).'
      : ' Le changement s\'appliquera au prochain envoi en paie.';
    const question = `Enregistrer pour la base ${c.base} : ${liste.join(' ; ')} ?${suite}`;
    if (!(await this.confirm.ask(question, 'Variables envoyées à la paie', 'Enregistrer'))) return;
    this.saving.set(true);
    this.svc.save(versEnregistrement(this.brouillon()), this.remiseAZero(), {
      heuresParJour: heuresModifiees ? heures : null,
      absenceCodes: lignesCodes.length ? { sansCode: this.codesAbsence().sansCode, codes: this.codesAbsence().codes } : null,
    }).subscribe({
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
    this.heuresParJour.set(c.heuresParJour ?? HEURES_PAR_JOUR_DEFAUT);
    this.codesAbsence.set(codesAbsenceLus(c.absenceCodes));
    this.loading.set(false);
    this.erreurs.set([]);
  }

  private maj(variable: string, f: (v: PayrollVariable) => PayrollVariable): void {
    this.brouillon.update((liste) => liste.map((v) => (v.variable === variable ? f(v) : v)));
    this.erreurs.set([]);
  }
}
