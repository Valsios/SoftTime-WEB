import { DOCUMENT } from '@angular/common';
import { inject, Injectable } from '@angular/core';

/** Une impression de rapport : nom du fichier proposé et rappels autour de la boîte d'impression. */
export interface PrintSession {
  /** Titre du document pendant l'impression : c'est le nom de fichier que propose « Enregistrer au format PDF ». */
  title: string;
  /** Passe l'écran en mode impression (pagination coupée, blocs d'impression rendus) avant l'ouverture de la boîte. */
  onStart(): void;
  /** Rend l'écran normal ; appelé une seule fois, à la fermeture de la boîte ou en dernier recours. */
  onEnd(): void;
}

/**
 * Impression d'un rapport par le navigateur (A4, thème clair) — motif d'origine de l'export PDF du
 * tableau de bord analytique, partagé par les écrans qui impriment un rapport.
 *
 * Pendant l'impression : le titre du document devient le nom de fichier, le thème est forcé en clair
 * (le PDF ne dépend jamais du thème de l'écran). À la fermeture de la boîte (`afterprint`), ou au bout
 * de {@link PrintReportService.FALLBACK_MS} si le navigateur n'émet pas l'événement, le titre et le
 * thème d'origine sont restaurés et `onEnd` est appelé — une seule fois.
 */
@Injectable({ providedIn: 'root' })
export class PrintReportService {
  /** Dernier recours si `afterprint` n'arrive jamais : l'écran ne reste pas en mode impression. */
  static readonly FALLBACK_MS = 300_000;

  private readonly doc = inject(DOCUMENT);

  print(session: PrintSession): void {
    const win = this.doc.defaultView;
    if (!win) return;
    const root = this.doc.documentElement;
    const previousTheme = root.getAttribute('data-theme');
    const previousTitle = this.doc.title;
    let restored = false;

    const restore = (): void => {
      if (restored) return;
      restored = true;
      this.doc.title = previousTitle;
      if (previousTheme) root.setAttribute('data-theme', previousTheme);
      else root.removeAttribute('data-theme');
      session.onEnd();
    };

    win.addEventListener('afterprint', restore, { once: true });
    win.setTimeout(restore, PrintReportService.FALLBACK_MS);

    this.doc.title = session.title;
    root.setAttribute('data-theme', 'light');
    session.onStart();

    try {
      win.print();
    } catch {
      restore();
    }
  }
}
