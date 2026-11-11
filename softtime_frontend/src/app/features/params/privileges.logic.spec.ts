import { describe, expect, it } from 'vitest';
import {
  buildRoleImpactMessage,
  canEditRolePermissions,
  countUsersOfRole,
  diffPermissions,
  extractErrorMessage,
  isAdminRole,
  resolveRoleLabel,
} from './privileges.logic';

describe('privileges.logic', () => {
  it('reconnaît le rôle administrateur', () => {
    expect(isAdminRole(1)).toBe(true);
    expect(isAdminRole(2)).toBe(false);
    expect(isAdminRole(null)).toBe(false);
    expect(isAdminRole(undefined)).toBe(false);
  });

  it('autorise la modification d’un rôle par un administrateur seulement', () => {
    expect(canEditRolePermissions(1, 2)).toBe(true);
    expect(canEditRolePermissions(1, 1)).toBe(false);
    expect(canEditRolePermissions(2, 2)).toBe(false);
    expect(canEditRolePermissions(null, 2)).toBe(false);
    expect(canEditRolePermissions(1, null)).toBe(false);
  });

  it('compte les utilisateurs d’un rôle', () => {
    expect(countUsersOfRole([{ roleId: 2 }], 3)).toBe(0);
    const users = [{ roleId: 1 }, { roleId: 2 }, { roleId: 2 }, { roleId: 3 }];
    expect(countUsersOfRole(users, 2)).toBe(2);
  });

  it('construit le message d’impact avec le nombre et les mentions attendues', () => {
    const message = buildRoleImpactMessage(3);
    expect(message).toContain('3 utilisateur(s)');
    expect(message).toContain('droits sont personnalisés');
    expect(message).toContain('dès leur prochaine action');
  });

  it('extrait le message d’erreur du serveur', () => {
    expect(extractErrorMessage({ error: 'Accès refusé' })).toBe('Accès refusé');
    expect(extractErrorMessage({ error: { message: 'Message serveur' } })).toBe('Message serveur');
    expect(extractErrorMessage({ error: { title: 'Titre serveur' } })).toBe('Titre serveur');
    expect(extractErrorMessage({ message: 'Message technique' })).toBe('Message technique');
    expect(extractErrorMessage(null)).toBe('Enregistrement refusé par le serveur.');
    expect(extractErrorMessage({})).toBe('Enregistrement refusé par le serveur.');
  });

  it('diffPermissions : permissions identiques', () => {
    const diff = diffPermissions(['A', 'B'], ['B', 'A']);
    expect(diff.added).toEqual([]);
    expect(diff.removed).toEqual([]);
    expect(diff.count).toBe(0);
  });

  it('diffPermissions : ajouts seuls', () => {
    const diff = diffPermissions(['A'], ['A', 'C', 'B']);
    expect(diff.added).toEqual(['B', 'C']);
    expect(diff.removed).toEqual([]);
    expect(diff.count).toBe(2);
  });

  it('diffPermissions : retraits seuls', () => {
    const diff = diffPermissions(['A', 'B', 'C'], ['B']);
    expect(diff.added).toEqual([]);
    expect(diff.removed).toEqual(['A', 'C']);
    expect(diff.count).toBe(2);
  });

  it('diffPermissions : mélange ajouts et retraits', () => {
    const diff = diffPermissions(['A', 'B'], ['B', 'C']);
    expect(diff.added).toEqual(['C']);
    expect(diff.removed).toEqual(['A']);
    expect(diff.count).toBe(2);
  });

  it('diffPermissions : listes renvoyées toujours triées', () => {
    const diff = diffPermissions(['Z', 'K', 'M'], ['A', 'K', 'Y']);
    expect(diff.added).toEqual(['A', 'Y']);
    expect(diff.removed).toEqual(['M', 'Z']);
    expect(diff.count).toBe(4);
  });

  it('resolveRoleLabel : rôle trouvé', () => {
    const roles = [
      { id: 1, nom: 'Administrateur' },
      { id: 2, nom: 'Responsable RH' },
    ];
    expect(resolveRoleLabel(roles, 1)).toBe('Administrateur');
    expect(resolveRoleLabel(roles, 2)).toBe('Responsable RH');
  });

  it('resolveRoleLabel : rôle introuvable', () => {
    const roles = [{ id: 1, nom: 'Administrateur' }];
    expect(resolveRoleLabel(roles, 10034)).toBe('Rôle #' + 10034);
  });

  it('resolveRoleLabel : liste vide', () => {
    expect(resolveRoleLabel([], 5)).toBe('Rôle #' + 5);
    expect(resolveRoleLabel([], null)).toBe('—');
  });
});
