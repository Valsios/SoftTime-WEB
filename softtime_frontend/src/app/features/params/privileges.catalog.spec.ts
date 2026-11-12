import { describe, expect, it } from 'vitest';
import { CatalogCategory, categoriesFromCatalog } from './privileges.catalog';

/** Catalogue des codes chargé depuis le serveur, avec repli sur la liste locale. */
describe('categoriesFromCatalog', () => {
  const local: CatalogCategory[] = [
    { key: 'PROC', title: 'Traitements', icon: 'grid', items: [{ code: 'PROC:SHIFTS', label: 'Shifts', canCreate: true }] },
    { key: 'SELF', title: 'Mon espace', icon: 'user-check', items: [{ code: 'SELF:READ', label: 'Mon espace' }] },
  ];

  it('construit pages et actions depuis les codes du serveur, avec les libellés locaux', () => {
    const result = categoriesFromCatalog(
      [{ key: 'PROC', gate: 3, codes: ['PROC:SHIFTS', 'PROC:SHIFTS:CREATE', 'PROC:SHIFTS:DELETE', 'PROC:PUNCHES'] }],
      local,
    );
    expect(result).toEqual([
      {
        key: 'PROC',
        title: 'Traitements',
        icon: 'grid',
        items: [
          { code: 'PROC:SHIFTS', label: 'Shifts', canCreate: true, canDelete: true },
          { code: 'PROC:PUNCHES', label: 'PROC:PUNCHES' },
        ],
      },
    ]);
  });

  it('domaine inconnu localement : affiché par sa clé', () => {
    const [nouveau] = categoriesFromCatalog([{ key: 'NEW', gate: 7, codes: ['NEW:PAGE'] }], local);
    expect(nouveau.title).toBe('NEW');
    expect(nouveau.items).toEqual([{ code: 'NEW:PAGE', label: 'NEW:PAGE' }]);
  });

  it('catalogue vide, absent ou illisible : la liste locale reste affichée', () => {
    expect(categoriesFromCatalog([], local)).toEqual(local);
    expect(categoriesFromCatalog(null, local)).toEqual(local);
    expect(categoriesFromCatalog([{ key: 'X', gate: 0, codes: [] }], local)).toEqual(local);
  });
});
