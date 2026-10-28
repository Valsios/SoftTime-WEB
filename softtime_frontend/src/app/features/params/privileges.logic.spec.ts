import { describe, expect, it } from 'vitest';
import {
  buildRoleImpactMessage,
  canEditRolePermissions,
  countUsersOfRole,
  extractErrorMessage,
  isAdminRole,
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
    expect(message).toContain('12 h maximum');
  });

  it('extrait le message d’erreur du serveur', () => {
    expect(extractErrorMessage({ error: 'Accès refusé' })).toBe('Accès refusé');
    expect(extractErrorMessage({ error: { message: 'Message serveur' } })).toBe('Message serveur');
    expect(extractErrorMessage({ error: { title: 'Titre serveur' } })).toBe('Titre serveur');
    expect(extractErrorMessage({ message: 'Message technique' })).toBe('Message technique');
    expect(extractErrorMessage(null)).toBe('Enregistrement refusé par le serveur.');
    expect(extractErrorMessage({})).toBe('Enregistrement refusé par le serveur.');
  });
});
