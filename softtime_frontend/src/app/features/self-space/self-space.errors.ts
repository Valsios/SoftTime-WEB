/**
 * Mon espace : erreurs de api/me/space/* transformées en encadré durable (texte seul, jamais le JSON brut).
 * Logique pure, testée par self-space.errors.spec.ts. Aucune donnée de salarié n'y passe : seulement un statut et le message du serveur.
 */

export type SelfErrorKind = 'account' | 'reconnect' | 'period' | 'server' | 'network';

export interface SelfErrorNotice {
  kind: SelfErrorKind;
  /** Message du serveur (texte seul) ou, à défaut, un message neutre. */
  message: string;
  /** Consigne à l'utilisateur. */
  hint: string;
  /** Vrai quand les quatre cartes ne doivent pas s'afficher (une carte vide serait trompeuse). */
  masksCards: boolean;
}

export const HINT_RH = 'Contactez la RH.';
export const HINT_RECONNECT = 'Reconnectez-vous.';
export const HINT_RETRY = 'Réessayez dans un instant.';
export const HINT_PERIOD = 'Choisissez un autre mois.';
export const MSG_PERIOD = 'La période demandée n’est pas valide.';
export const MSG_SERVER = 'Mon espace est momentanément indisponible.';
export const MSG_NETWORK = 'Le serveur est injoignable.';
export const MSG_ACCOUNT = 'L’accès à Mon espace est refusé pour ce compte.';

const MAX_LENGTH = 300;

interface ErrorLike {
  status?: number;
  error?: unknown;
}

/** Texte lisible du serveur : champ « error » (ou « message ») d'un corps JSON ; jamais un JSON, du HTML ni un message technique. */
export function serverMessage(err: unknown): string | null {
  const body = (err as ErrorLike | null)?.error;
  let texte: unknown = null;
  if (body !== null && typeof body === 'object') {
    const o = body as Record<string, unknown>;
    texte = typeof o['error'] === 'string' ? o['error'] : typeof o['message'] === 'string' ? o['message'] : null;
  } else if (typeof body === 'string') {
    texte = body;
  }
  if (typeof texte !== 'string') return null;
  const propre = texte.trim();
  if (!propre || propre.length > MAX_LENGTH || /^[{[<]/.test(propre)) return null;
  return propre;
}

export function describeSelfError(err: unknown): SelfErrorNotice {
  const status = (err as ErrorLike | null)?.status;
  const message = serverMessage(err);

  if (status === undefined || status === null || status === 0) {
    return { kind: 'network', message: MSG_NETWORK, hint: HINT_RETRY, masksCards: false };
  }
  if (status === 401 || (status === 403 && message !== null && /reconnect/i.test(message))) {
    return { kind: 'reconnect', message: message ?? 'Votre session n’est plus valide.', hint: HINT_RECONNECT, masksCards: true };
  }
  if (status === 403) {
    return { kind: 'account', message: message ?? MSG_ACCOUNT, hint: HINT_RH, masksCards: true };
  }
  if (status === 400) {
    return { kind: 'period', message: message ?? MSG_PERIOD, hint: HINT_PERIOD, masksCards: true };
  }
  return { kind: 'server', message: message ?? MSG_SERVER, hint: HINT_RETRY, masksCards: false };
}

const RANK: Record<SelfErrorKind, number> = { reconnect: 5, account: 4, period: 3, server: 2, network: 1 };

/** Quatre lectures peuvent échouer en même temps : on garde l'erreur la plus significative pour l'utilisateur. */
export function strongestNotice(current: SelfErrorNotice | null, next: SelfErrorNotice): SelfErrorNotice {
  return current !== null && RANK[current.kind] >= RANK[next.kind] ? current : next;
}
