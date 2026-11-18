import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { PayrollClosureState } from '../models';
import { PayrollClosureService } from '../services/payroll-closure.service';

/** Jour aaaa-mm-jj en jj/mm/aaaa. */
function jour(iso: string): string {
  const m = /^(\d{4})-(\d{2})-(\d{2})/.exec(iso ?? '');
  return m ? `${m[3]}/${m[2]}/${m[1]}` : iso;
}

/** Infobulle des boutons désactivés par une période clôturée. */
export const VERROU_CLOTURE =
  'Période clôturée : action désactivée. La clôture est définitive ; les corrections se font sur la période suivante.';

/**
 * Texte du bandeau, en une phrase qui suit le titre « Période clôturée » sans le répéter : la période, aucune
 * modification possible, correction sur la période suivante.
 */
export function texteBandeau(debut: string, fin: string): string {
  return `Du ${jour(debut)} au ${jour(fin)}, aucune modification n'est possible : `
    + 'les corrections se font sur la période suivante.';
}

/**
 * Bandeau « Période clôturée » des pages qui modifient les sources de la paie (Heures supplémentaires, Heures corrigées,
 * Pointages, Anomalies, Validation semaine). Il lit l'état de la sélection (silencieux) et émet `closedChange` pour que la
 * page désactive ses boutons de modification. Le serveur refuse de toute façon chaque modification (409).
 */
@Component({
  selector: 'closed-period-banner',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (etat(); as e) {
      @if (e.cloturee && e.periodes.length) {
        <p class="cpb" role="status" aria-live="polite">
          <strong class="cpb__title">Période clôturée</strong>
          <span class="cpb__text">{{ texte(e) }}</span>
        </p>
      }
    }
  `,
  styles: [`
    :host { display: block; }
    .cpb { display: flex; flex-wrap: wrap; gap: 4px 10px; align-items: baseline; margin: 0 0 var(--space-md); padding: 10px 14px;
      font-size: 13px; border-radius: var(--radius-md); border: 1px solid var(--warning-border); color: var(--warning);
      background: var(--warning-bg); }
    .cpb__title { font-weight: 600; white-space: nowrap; }
    .cpb__text { min-width: 0; overflow-wrap: anywhere; color: var(--app-text); }
  `],
})
export class ClosedPeriodBanner {
  private readonly svc = inject(PayrollClosureService);

  /** Sélection de la page (dates aaaa-mm-jj) ; null tant qu'aucune recherche n'a été faite. */
  readonly period = input<{ from: string; to: string } | null>(null);
  /** Vrai quand la sélection recoupe une période clôturée de la base active. */
  readonly closedChange = output<boolean>();

  readonly etat = signal<PayrollClosureState | null>(null);
  private seq = 0;

  constructor() {
    effect(() => {
      const p = this.period();
      untracked(() => this.lire(p));
    });
  }

  texte(e: PayrollClosureState): string {
    const p = e.periodes[0];
    return texteBandeau(p.debut, p.fin);
  }

  private lire(p: { from: string; to: string } | null): void {
    const seq = ++this.seq;
    if (!p?.from || !p?.to) {
      this.appliquer(null);
      return;
    }
    this.svc.state(p.from.slice(0, 10), p.to.slice(0, 10)).subscribe({
      next: (e) => { if (seq === this.seq) this.appliquer(e); },
      error: () => { if (seq === this.seq) this.appliquer(null); },
    });
  }

  private appliquer(e: PayrollClosureState | null): void {
    this.etat.set(e);
    this.closedChange.emit(!!e?.cloturee);
  }
}
