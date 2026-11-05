import type { PasswordPolicy, PasswordPolicyRule } from '../models';

/** Règle évaluée côté client : le libellé vient du serveur, le résultat du contrôle local. */
export interface PasswordRuleCheck extends PasswordPolicyRule {
  ok: boolean;
}

/**
 * Repli utilisé uniquement si `GET /api/auth/password-policy` est injoignable.
 * En fonctionnement normal, les règles et libellés viennent du serveur.
 */
export const FALLBACK_PASSWORD_POLICY: PasswordPolicy = {
  minLength: 8,
  rules: [
    { code: 'length', label: 'Au moins 8 caractères' },
    { code: 'uppercase', label: 'Au moins une majuscule' },
    { code: 'lowercase', label: 'Au moins une minuscule' },
    { code: 'digit', label: 'Au moins un chiffre' },
    { code: 'special', label: 'Au moins un caractère spécial (!, @, #, …)' },
  ],
};

/** Évalue chaque règle de la politique pour le mot de passe saisi. */
export function evaluatePasswordPolicy(password: string, policy: PasswordPolicy): PasswordRuleCheck[] {
  return policy.rules.map((rule) => ({
    ...rule,
    ok: isRuleSatisfied(rule.code, password, policy),
  }));
}

/** Toutes les règles sont-elles satisfaites ? */
export function isPasswordCompliant(checks: readonly PasswordRuleCheck[]): boolean {
  return checks.length > 0 && checks.every((check) => check.ok);
}

function isRuleSatisfied(code: string, password: string, policy: PasswordPolicy): boolean {
  switch (code) {
    case 'length':
      return password.length >= policy.minLength;
    case 'uppercase':
      return /\p{Lu}/u.test(password);
    case 'lowercase':
      return /\p{Ll}/u.test(password);
    case 'digit':
      return /\p{Nd}/u.test(password);
    case 'special':
      // Un espace ne compte pas comme caractère spécial, comme côté serveur.
      return /[^\p{L}\p{Nd}\s]/u.test(password);
    default:
      // Règle inconnue (politique plus récente que le client) : on ne bloque pas l'utilisateur.
      return true;
  }
}
