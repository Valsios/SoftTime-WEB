/**
 * Mot de passe SQL des bases (SAGE / pointeuse) : le serveur ne le renvoie jamais.
 * Il indique seulement `passwordDefined`. Le client n'envoie un mot de passe que s'il vient d'être saisi.
 */

export interface ConnectionFormLike {
  id?: number | null;
  serveur?: string | null;
  login?: string | null;
  sqlAuth?: boolean | null;
  password?: string | null;
  passwordDefined?: boolean | null;
}

export type ConnectionKind = 'SAGE' | 'POINTEUSE';

/** Copie sans mot de passe (défense en profondeur : aucun mot de passe en mémoire après chargement d'une base). */
export function withoutPassword<T extends { password?: string | null }>(db: T): T {
  const { password: _password, ...rest } = db;
  return rest as T;
}

export function stripDatabasePasswords<T extends { password?: string | null }>(dbs: readonly T[] | null | undefined): T[] {
  return (dbs ?? []).map(withoutPassword);
}

/** Base existante dont le mot de passe est stocké côté serveur et n'est pas en cours de modification. */
export function usesStoredPassword(form: ConnectionFormLike, editingPassword: boolean): boolean {
  return (form.id ?? 0) > 0 && form.passwordDefined === true && !editingPassword && (form.sqlAuth ?? true);
}

/** Vrai si serveur, login ou mode d'authentification diffèrent de la base chargée. */
export function connectionTargetChanged(form: ConnectionFormLike, initial: ConnectionFormLike | null): boolean {
  if (!initial) return false;
  const norm = (v: string | null | undefined) => (v ?? '').trim().toLowerCase();
  return norm(form.serveur) !== norm(initial.serveur)
    || norm(form.login) !== norm(initial.login)
    || (form.sqlAuth ?? true) !== (initial.sqlAuth ?? true);
}

/** Paramètres d'authentification pour la découverte (bases, tables, colonnes). */
export function discoveryAuth(form: ConnectionFormLike, kind: ConnectionKind, editingPassword: boolean): {
  sqlAuth: boolean;
  login: string | null;
  password: string | null;
  connectionId?: number;
  connectionKind?: ConnectionKind;
} {
  const sqlAuth = form.sqlAuth ?? true;
  const auth = {
    sqlAuth,
    login: sqlAuth ? (form.login ?? null) : null,
    password: sqlAuth && editingPassword && form.password ? form.password : null,
  };
  return usesStoredPassword(form, editingPassword)
    ? { ...auth, connectionId: form.id as number, connectionKind: kind }
    : auth;
}

/**
 * Corps envoyé à l'enregistrement ou au test : le mot de passe n'y figure que s'il a été saisi.
 * `passwordDefined` est une information du serveur, jamais renvoyée.
 */
export function connectionPayload<T extends ConnectionFormLike>(form: T, editingPassword: boolean): Omit<T, 'passwordDefined'> {
  const { passwordDefined: _defined, password, ...rest } = form;
  return (editingPassword && password ? { ...rest, password } : rest) as Omit<T, 'passwordDefined'>;
}
