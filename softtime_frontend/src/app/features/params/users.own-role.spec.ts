import { describe, expect, it } from 'vitest';
import { OWN_ROLE_HINT, isEditingOwnAccount, roleFieldHint } from './role-change.logic';

/** Écran Utilisateurs : un compte ne peut pas changer son propre rôle (le serveur le refuse, « Vous ne pouvez pas modifier votre propre rôle. »). */
describe('Utilisateurs : son propre rôle', () => {
  it('le champ Rôle est désactivé seulement pour le compte connecté', () => {
    expect(isEditingOwnAccount(14, 14)).toBe(true);
    expect(isEditingOwnAccount(15, 14)).toBe(false);
    expect(isEditingOwnAccount(null, 14)).toBe(false);          // création d'un compte
    expect(isEditingOwnAccount(14, undefined)).toBe(false);     // session sans identifiant
  });

  it("l'aide du champ explique pourquoi son propre rôle n'est pas modifiable", () => {
    expect(roleFieldHint(true, 5)).toBe(OWN_ROLE_HINT);
    expect(OWN_ROLE_HINT).toContain('propre rôle');
  });

  it("autres comptes : aide inchangée (vide, ou invitation à créer un rôle s'il n'y en a aucun)", () => {
    expect(roleFieldHint(false, 3)).toBe('');
    expect(roleFieldHint(false, 0)).toBe('Aucun rôle disponible : créez un rôle avant de créer un utilisateur.');
  });
});
