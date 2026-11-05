import { effect, inject, Injectable, signal, untracked } from '@angular/core';
import { SessionStore } from '../../core/session.store';
import { ClosingChecklist, ClosingReport } from '../../shared/models';
import { ClosingFilter, ClosingSort } from './closing.logic';

/** Dernier résultat d'analyse conservé en mémoire : filtre appliqué, rapport et heure du calcul. */
export interface ClosingResult {
  filter: ClosingFilter;
  report: ClosingReport;
  analysedAt: Date;
}

/**
 * État du Contrôle avant paie conservé pendant la session (mémoire de l'application, aucun stockage
 * navigateur) : le filtre en cours de saisie, le dernier résultat et ses options d'affichage. Quitter
 * la page puis y revenir réaffiche donc le même résultat sans relancer d'analyse.
 *
 * L'état est vidé dès que le contexte change : déconnexion, autre compte ou autre base (SAGE ou
 * pointage). Il observe pour cela les signaux existants de {@link SessionStore}, sans le modifier.
 */
@Injectable({ providedIn: 'root' })
export class ClosingSessionState {
  private readonly session = inject(SessionStore);
  private contextKey: string | null = null;

  /** Filtre en cours de saisie (peut différer du résultat affiché). */
  readonly draft = signal<ClosingFilter | null>(null);
  readonly result = signal<ClosingResult | null>(null);
  readonly tab = signal<'afix' | 'all'>('afix');
  /** Dernière checklist d'avancement chargée avec l'analyse (null : aucune, ou en échec). */
  readonly checklist = signal<ClosingChecklist | null>(null);
  /** Niveaux de gravité retenus dans la liste (vide : aucun filtre) et tri choisi. */
  readonly gravites = signal<string[]>([]);
  readonly tri = signal<ClosingSort>('date');
  readonly page = signal(1);
  readonly pageSize = signal<number | null>(null);

  constructor() {
    this.contextKey = this.currentKey();
    effect(() => {
      this.currentKey();
      untracked(() => this.syncContext());
    });
  }

  /** Compare le contexte courant au contexte du résultat conservé ; vide l'état s'ils diffèrent. */
  syncContext(): string | null {
    const key = this.currentKey();
    if (key !== this.contextKey) {
      this.contextKey = key;
      this.reset();
    }
    return key;
  }

  /** Contexte au moment où une analyse démarre : sert à écarter un résultat arrivé après un changement de base ou de compte. */
  currentContext(): string | null {
    return this.syncContext();
  }

  /** Conserve un résultat, sauf si le contexte a changé depuis le lancement de l'analyse. */
  storeResult(result: ClosingResult, startedIn: string | null): boolean {
    if (this.syncContext() !== startedIn) return false;
    this.result.set(result);
    this.draft.set(result.filter);
    return true;
  }

  /** Conserve la checklist, sauf si le contexte a changé depuis son lancement. */
  storeChecklist(checklist: ClosingChecklist | null, startedIn: string | null): boolean {
    if (this.syncContext() !== startedIn) return false;
    this.checklist.set(checklist);
    return true;
  }

  reset(): void {
    this.checklist.set(null);
    this.draft.set(null);
    this.result.set(null);
    this.tab.set('afix');
    this.gravites.set([]);
    this.tri.set('date');
    this.page.set(1);
    this.pageSize.set(null);
  }

  private currentKey(): string | null {
    if (!this.session.token()) return null;
    const user = this.session.session()?.userId ?? '';
    return `${user}|${this.session.activeSageDb() ?? ''}|${this.session.activePointeuseDb() ?? ''}`;
  }
}
