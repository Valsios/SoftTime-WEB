import { ChangeDetectionStrategy, Component, computed, effect, inject, input, signal, untracked } from '@angular/core';
import { RouterLink } from '@angular/router';
import { errorMessage } from '../../core/error.logic';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';
import { PayrollPeriods } from '../../shared/models';
import { SoftBadge, SoftCard } from '../../shared/components';
import { PayrollClosureService } from '../../shared/services/payroll-closure.service';
import {
  ANCRE_CLOTURE,
  aujourdhuiIso,
  etapeSuivante,
  peutVoirCloture,
  RAPPEL_CLOTURE_BASE_ENTIERE,
  TITRE_CARTE_CLOTURE,
} from '../params/payroll-closure.logic';

/**
 * Bloc « Étape suivante : clôture » du Contrôle avant paie. LECTURE SEULE : il lit les périodes envoyées
 * (PayrollClosureService.periods) et affiche l'état de la période choisie, puis renvoie vers la carte de clôture de la page
 * Bases SAGE. Il ne contient aucune action d'écriture ; la clôture se fait uniquement dans la carte. Visible sous les mêmes
 * conditions que la carte (base active et droit de la voir), sinon absent.
 */
@Component({
  selector: 'app-closure-next-step',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, SoftBadge, SoftCard],
  template: `
    @if (visible()) {
      <soft-card variant="bordered" padding="sm">
        <section class="cns" aria-labelledby="cns-title">
          <div class="cns__head">
            <h2 id="cns-title" class="cns__title">Étape suivante : clôture</h2>
            @if (etape(); as e) {
              <soft-badge [variant]="e.etat === 'CLOTUREE' ? 'success' : e.etat === 'ENVOYEE' ? 'info' : 'muted'">{{ badge(e.etat) }}</soft-badge>
            }
          </div>
          @if (erreur()) {
            <p class="cns__line" role="status">L'état de la clôture n'a pas pu être lu : {{ erreur() }}</p>
          } @else if (etape(); as e) {
            <p class="cns__line" role="status">{{ e.phrase }}</p>
            @if (e.indication) { <p class="cns__muted">{{ e.indication }}</p> }
          } @else {
            <p class="cns__muted" role="status">Lecture de l'état de la période…</p>
          }
          <p class="cns__muted">{{ rappel }}</p>
          <a class="soft-btn soft-btn--secondary soft-btn--sm cns__link" routerLink="/sage-databases" [fragment]="ancre"
             [attr.aria-label]="'Aller à la clôture : carte « ' + titreCarte + ' » de la page Bases SAGE'">Aller à la clôture</a>
        </section>
      </soft-card>
    }
  `,
  styles: [`
    :host { display: block; }
    .cns { display: grid; gap: var(--space-xs); min-width: 0; }
    .cns__head { display: flex; align-items: center; flex-wrap: wrap; gap: var(--space-sm); }
    .cns__title { margin: 0; font-family: var(--font-heading); font-size: 16px; font-weight: 600; color: var(--app-text-strong); }
    .cns__line { margin: 0; font-size: 14px; color: var(--app-text); overflow-wrap: anywhere; }
    .cns__muted { margin: 0; font-size: 13px; color: var(--text-muted); overflow-wrap: anywhere; }
    .cns__link { justify-self: start; margin-top: var(--space-xs); }
    @media (max-width: 767px) { .cns__link { justify-self: stretch; text-align: center; } }
  `],
})
export class ClosureNextStep {
  private readonly svc = inject(PayrollClosureService);
  private readonly session = inject(SessionStore);

  /** Bornes de la période choisie dans le Contrôle (aaaa-mm-jj). */
  readonly from = input.required<string>();
  readonly to = input.required<string>();

  readonly rappel = RAPPEL_CLOTURE_BASE_ENTIERE;
  readonly ancre = ANCRE_CLOTURE;
  readonly titreCarte = TITRE_CARTE_CLOTURE;

  private readonly donnees = signal<PayrollPeriods | null>(null);
  readonly erreur = signal<string | null>(null);

  readonly visible = computed(() =>
    peutVoirCloture(this.session.activeSageDb(), this.session.permissions(), this.session.hasRight(Droit.Databases)));

  readonly etape = computed(() => {
    const d = this.donnees();
    return d ? etapeSuivante(d.periodes, this.from().slice(0, 10), this.to().slice(0, 10), aujourdhuiIso()) : null;
  });

  constructor() {
    // Lecture à l'affichage et à chaque changement de base ou de visibilité ; jamais d'écriture.
    effect(() => {
      const visible = this.visible();
      this.session.activeSageDb();
      untracked(() => (visible ? this.charger() : this.donnees.set(null)));
    });
  }

  badge(etat: 'AUCUN' | 'ENVOYEE' | 'CLOTUREE'): string {
    return etat === 'CLOTUREE' ? 'Clôturée' : etat === 'ENVOYEE' ? 'Envoyée' : 'Non envoyée';
  }

  private charger(): void {
    this.erreur.set(null);
    this.donnees.set(null);
    this.svc.periods().subscribe({
      next: (d) => this.donnees.set(d),
      error: (e) => this.erreur.set(errorMessage(e)),
    });
  }
}
