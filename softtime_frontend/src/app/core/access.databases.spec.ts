import { describe, expect, it } from 'vitest';
import { databaseCode, grantsDatabase } from './access.logic';

describe('droits par base (identifiant et joker)', () => {
  it('fabrique le code par identifiant ou pour toutes les bases', () => {
    expect(databaseCode('sage', 12, 'read')).toBe('sage.12.read');
    expect(databaseCode('pointeuse', 'all', 'delete')).toBe('pointeuse.all.delete');
  });

  it('accorde une base par son identifiant', () => {
    expect(grantsDatabase(['sage.12.update'], 'sage', 12, 'update')).toBe(true);
    expect(grantsDatabase(['sage.12.update'], 'sage', 13, 'update')).toBe(false);
    expect(grantsDatabase(['sage.12.update'], 'sage', 12, 'delete')).toBe(false);
  });

  it("le joker « all » couvre toute base du même type, même ajoutée plus tard", () => {
    expect(grantsDatabase(['sage.all.read'], 'sage', 99, 'read')).toBe(true);
    expect(grantsDatabase(['sage.all.read'], 'pointeuse', 99, 'read')).toBe(false);
    expect(grantsDatabase(['sage.all.read'], 'sage', 99, 'update')).toBe(false);
  });
});
