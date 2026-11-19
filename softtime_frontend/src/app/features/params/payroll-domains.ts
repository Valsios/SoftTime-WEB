import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { errorMessage } from '../../core/error.logic';
import { ToastService } from '../../core/toast.service';
import { PayrollDomains, PayrollVariablesConfig } from '../../shared/models';
import { PayrollVariablesService } from '../../shared/services/payroll-variables.service';
import { SoftButton } from '../../shared/components';

/** Libellé d'un changement d'activation ou de code, pour la confirmation. */
export function questionDomaines(base: string, avant: PayrollDomains, apres: PayrollDomains): string {
  const etat = (actif: boolean) => (actif ? 'exigée à la clôture' : 'non exigée');
  const morceaux: string[] = [];
  const code = (apres.codeCantine ?? '').trim();
  if (apres.codeCantine != null && code !== (avant.codeCantine ?? '').trim()) morceaux.push(`code de constante de la cantine ${code}`);
  if (avant.cantine !== apres.cantine) morceaux.push(`cantine ${etat(apres.cantine)}`);
  if (avant.frais !== apres.frais) morceaux.push(`frais de déplacement ${apres.frais ? 'exigés à la clôture' : 'non exigés'}`);
  return `Enregistrer pour la base ${base} : ${morceaux.join(' ; ')} ? Le réglage ne change rien à ce qui est envoyé en paie, `
    + 'seulement ce que la clôture exige.';
}

/**
 * « Domaines envoyés à la paie » de la base active (sous les variables) : la cantine et les frais de déplacement activés
 * sont exigés à la clôture (dernier envoi réussi, sans plage de matricules, sur les bornes exactes de la période).
 * Désactivés par défaut ; seule une base STANDARD les envoie. Enregistrement séparé des variables, sans verrou de mois.
 */
@Component({
  selector: 'app-payroll-domains',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SoftButton],
  template: `
    @if (config(); as c) {
      <fieldset class="pd">
        <legend class="pd__title">Domaines envoyés à la paie</legend>
        @if (standard()) {
          <p class="soft-field__hint pd__hint">
            Cochez la cantine et les frais de déplacement si cette base les envoie à la paie : la clôture exigera alors leur
            dernier envoi réussi, sans plage de matricules, sur les bornes exactes de la période. Un domaine envoyé sur la
            période est exigé même s'il n'est pas coché.
          </p>
          <label class="soft-field pd__code">
            <span class="soft-field__label">Code de constante de la cantine</span>
            <input class="soft-input" type="text" autocomplete="off" spellcheck="false" [value]="code()" [disabled]="saving()"
              (input)="code.set($any($event.target).value)" />
            <span class="soft-field__hint">
              Code de la constante SAGE qui reçoit la cantine (10 caractères au plus), à créer d'abord dans SAGE.
              @if (c.domaines?.codeCantineParDefaut) { Aucun code configuré : le code historique {{ c.domaines?.codeCantine }} n'existe dans aucune base SAGE. }
            </span>
          </label>
          <div class="pd__rows">
            <label class="pd__row">
              <input type="checkbox" [checked]="cantine()" [disabled]="saving()" (change)="cantine.set($any($event.target).checked)" />
              <span>Cantine</span>
            </label>
            <label class="pd__row">
              <input type="checkbox" [checked]="frais()" [disabled]="saving()" (change)="frais.set($any($event.target).checked)" />
              <span>Frais de déplacement</span>
            </label>
            <soft-button size="sm" [loading]="saving()" [disabled]="!modifie()" (click)="enregistrer()">Enregistrer les domaines</soft-button>
          </div>
          @if (erreur(); as e) { <p class="pd__error" role="alert">{{ e }}</p> }
        } @else {
          <p class="soft-field__hint pd__hint">
            Base AUTRE : la cantine et les frais de déplacement ne s'y envoient pas ; la clôture ne les exige pas.
          </p>
        }
      </fieldset>
    }
  `,
  styles: [`
    :host { display: block; }
    .pd { margin: var(--space-md) 0 0; padding: var(--space-md); border: 1px solid var(--color-border); border-radius: var(--radius-lg);
      min-width: 0; }
    .pd__title { padding: 0 var(--space-xs); font-weight: 600; color: var(--app-text-strong); font-size: 14px; }
    .pd__hint { margin: 0 0 var(--space-sm); }
    .pd__code { display: grid; gap: 4px; max-width: 420px; margin: 0 0 var(--space-sm); min-width: 0; }
    .pd__code input { width: 100%; min-width: 0; box-sizing: border-box; }
    .pd__rows { display: flex; flex-wrap: wrap; align-items: center; gap: var(--space-sm) var(--space-lg); }
    .pd__row { display: inline-flex; align-items: center; gap: var(--space-xs); font-size: 14px; color: var(--app-text); cursor: pointer; }
    .pd__row:has(input:disabled) { cursor: not-allowed; }
    .pd__error { margin: var(--space-sm) 0 0; color: var(--danger); font-size: 13px; overflow-wrap: anywhere; }
    @media (max-width: 767px) {
      .pd__rows { flex-direction: column; align-items: flex-start; }
    }
  `],
})
export class PayrollDomainsSetting {
  private readonly svc = inject(PayrollVariablesService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  /** Configuration de la base active (réglage enregistré et type de base). */
  readonly config = input<PayrollVariablesConfig | null>(null);
  /** Émis après un enregistrement : la carte des variables se recharge. */
  readonly enregistre = output<void>();

  readonly cantine = signal(false);
  readonly frais = signal(false);
  /** Code de constante de la cantine saisi (initialisé au code utilisé par la base). */
  readonly code = signal('');
  readonly saving = signal(false);
  readonly erreur = signal<string | null>(null);

  readonly standard = computed(() => (this.config()?.typeBase ?? '').toUpperCase() !== 'AUTRE');
  private readonly enregistres = computed<PayrollDomains>(() => ({
    cantine: !!this.config()?.domaines?.cantine,
    frais: !!this.config()?.domaines?.frais,
    codeCantine: this.config()?.domaines?.codeCantine ?? '',
  }));
  private readonly codeModifie = computed(() => this.code().trim() !== (this.enregistres().codeCantine ?? '').trim());
  readonly modifie = computed(() => this.cantine() !== this.enregistres().cantine || this.frais() !== this.enregistres().frais
    || this.codeModifie());

  constructor() {
    effect(() => {
      const d = this.enregistres();
      untracked(() => { this.cantine.set(d.cantine); this.frais.set(d.frais); this.code.set(d.codeCantine ?? ''); });
    });
  }

  async enregistrer(): Promise<void> {
    const c = this.config();
    if (!c || !this.modifie() || this.saving()) return;
    // Code envoyé seulement s'il a changé : absent, le serveur le laisse inchangé.
    const demande: PayrollDomains = { cantine: this.cantine(), frais: this.frais(), ...(this.codeModifie() ? { codeCantine: this.code().trim() } : {}) };
    this.erreur.set(null);
    if (!(await this.confirm.ask(questionDomaines(c.base, this.enregistres(), demande), 'Domaines envoyés à la paie', 'Enregistrer'))) return;
    this.saving.set(true);
    this.svc.saveDomains(demande).subscribe({
      next: () => {
        this.saving.set(false);
        this.toast.success('Domaines enregistrés.');
        this.enregistre.emit();
      },
      error: (e) => {
        this.saving.set(false);
        this.erreur.set(errorMessage(e));
      },
    });
  }
}
