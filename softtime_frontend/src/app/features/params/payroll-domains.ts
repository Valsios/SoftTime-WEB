import { computed, effect, inject, signal, Signal, untracked } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { errorMessage } from '../../core/error.logic';
import { ToastService } from '../../core/toast.service';
import { PayrollDomains, PayrollVariablesConfig, SageConstantOption } from '../../shared/models';
import { PayrollVariablesService } from '../../shared/services/payroll-variables.service';
import { SelectOption } from '../../shared/components/soft-select';

/** Code de paie des frais de déplacement : fixe, affiché mais non configurable. */
export const CODE_FRAIS = 'HA06';
/** Mention affichée à côté du code des frais. */
export const MENTION_CODE_FIXE = 'code fixe';
export const BADGE_ENVOYE = 'envoyé à la paie';
export const BADGE_NON_ENVOYE = 'non envoyé à la paie';
export const MESSAGE_AUCUNE_CONSTANTE = 'Aucune constante dans cette base SAGE : créez d\'abord la constante de la cantine dans SAGE.';
export const MESSAGE_CODE_REQUIS = "Choisissez d'abord le code de paie.";
export const MESSAGE_CODE_ABSENT = 'Le code enregistré n\'existe plus dans SAGE : choisissez une constante existante pour activer la cantine.';
export const MENTION_INTROUVABLE = 'introuvable dans SAGE';

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
 * « Domaines envoyés à la paie » de la base active : la cantine et les frais de déplacement activés sont exigés à la clôture
 * (dernier envoi réussi, sans plage de matricules, sur les bornes exactes de la période). Désactivés par défaut ; seule une base
 * STANDARD les envoie. Les lignes sont affichées avec celles des variables (payroll-variables.ts) ; cette classe porte leur
 * brouillon et leur enregistrement, séparé des variables et sans verrou de mois. La liste des constantes n'est qu'une aide : le
 * serveur valide le code (non vide, 10 caractères au plus, présent dans T_CST) et refuse l'activation sans code valide.
 * À créer dans un contexte d'injection.
 */
export class DomainesPaie {
  private readonly svc = inject(PayrollVariablesService);
  private readonly toast = inject(ToastService);
  private readonly confirm = inject(ConfirmService);

  readonly cantine = signal(false);
  readonly frais = signal(false);
  /** Code de constante de la cantine choisi ; vide tant qu'aucun code n'est configuré (le code historique n'est jamais proposé). */
  readonly code = signal('');
  readonly saving = signal(false);
  readonly erreur = signal<string | null>(null);

  readonly standard = computed(() => (this.config()?.typeBase ?? '').toUpperCase() !== 'AUTRE');
  private readonly sauveCantine = computed(() => !!this.config()?.domaines?.cantine);
  private readonly sauveFrais = computed(() => !!this.config()?.domaines?.frais);
  private readonly sauveCode = computed(() => {
    const d = this.config()?.domaines;
    return d?.codeCantineParDefaut ? '' : (d?.codeCantine ?? '').trim();
  });
  private readonly enregistres = computed<PayrollDomains>(() => ({ cantine: this.sauveCantine(), frais: this.sauveFrais(), codeCantine: this.sauveCode() }));
  private readonly codeModifie = computed(() => this.code().trim() !== this.sauveCode());
  readonly modifie = computed(() => this.cantine() !== this.sauveCantine() || this.frais() !== this.sauveFrais() || this.codeModifie());

  /** Constantes de la base SAGE (T_CST). */
  readonly constantes = computed<SageConstantOption[]>(() => this.config()?.codesPaie ?? []);
  readonly avertissementCodes = computed(() => this.config()?.avertissementCodes ?? null);
  /** Aucune constante lisible dans la base SAGE : la cantine ne peut pas être activée. */
  readonly listeVide = computed(() => this.constantes().length === 0 && !this.avertissementCodes());
  /** Le code choisi n'existe pas (ou plus) dans la liste. */
  readonly codeAbsent = computed(() => {
    const code = this.code().trim().toLowerCase();
    return !!code && !this.avertissementCodes() && !this.constantes().some((o) => o.code.trim().toLowerCase() === code);
  });
  private readonly codeValide = computed(() => !!this.code().trim() && !this.codeAbsent() && !this.listeVide());
  /** Options de la liste : le code déjà enregistré reste visible, avec un avertissement, même s'il n'existe plus. */
  readonly options = computed<SelectOption[]>(() => {
    const liste = this.constantes().map((o) => ({ value: o.code, label: o.intitule ? `${o.code} — ${o.intitule}` : o.code }));
    return this.codeAbsent() ? [{ value: this.code().trim(), label: `${this.code().trim()} — ${MENTION_INTROUVABLE}` }, ...liste] : liste;
  });
  /** « (aucun) » n'est proposé que tant qu'aucun code n'est enregistré : le serveur refuse un code vide. */
  readonly peutVider = computed(() => !this.sauveCode());
  /** Libellé complet du code choisi (infobulle). */
  readonly titreCode = computed<string | null>(() => this.options().find((o) => o.value === this.code())?.label ?? null);
  /** Pourquoi l'interrupteur de la cantine ne peut pas être allumé ; null s'il le peut (ou s'il est déjà allumé). */
  readonly raisonCantine = computed<string | null>(() => {
    if (this.cantine()) return null;
    if (this.avertissementCodes()) return this.avertissementCodes();
    if (this.listeVide()) return MESSAGE_AUCUNE_CONSTANTE;
    if (!this.code().trim()) return MESSAGE_CODE_REQUIS;
    return this.codeAbsent() ? MESSAGE_CODE_ABSENT : null;
  });
  readonly cantineCochable = computed(() => !this.saving() && (this.cantine() || this.codeValide()));

  constructor(private readonly config: Signal<PayrollVariablesConfig | null>, private readonly recharger: () => void) {
    // Le brouillon n'est remplacé que si le réglage enregistré change (valeurs, pas objet) : un rechargement des variables le garde.
    effect(() => {
      const d = this.enregistres();
      untracked(() => { this.cantine.set(d.cantine); this.frais.set(d.frais); this.code.set(d.codeCantine ?? ''); });
    });
  }

  basculerCantine(actif: boolean): void {
    this.cantine.set(actif);
    this.erreur.set(null);
  }

  basculerFrais(actif: boolean): void {
    this.frais.set(actif);
    this.erreur.set(null);
  }

  choisirCode(code: string | null): void {
    this.code.set((code ?? '').trim());
    this.erreur.set(null);
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
        this.recharger();
      },
      error: (e) => {
        this.saving.set(false);
        this.erreur.set(errorMessage(e));
      },
    });
  }
}
