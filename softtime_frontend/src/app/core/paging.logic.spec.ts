import { describe, expect, it } from 'vitest';
import { mergePages, remainingPages } from './paging.logic';

describe('paging.logic', () => {
  it('une liste d’une page ou vide ne demande aucune page supplémentaire', () => {
    expect(remainingPages(0)).toEqual([]);
    expect(remainingPages(1)).toEqual([]);
    expect(remainingPages(Number.NaN)).toEqual([]);
  });

  it('les pages restantes vont de 2 à la dernière', () => {
    expect(remainingPages(4)).toEqual([2, 3, 4]);
  });

  it('les lignes sont remises dans l’ordre des pages', () => {
    const pages = [
      { page: 3, items: ['e'] },
      { page: 1, items: ['a', 'b'] },
      { page: 2, items: ['c', 'd'] },
    ];
    expect(mergePages(pages)).toEqual(['a', 'b', 'c', 'd', 'e']);
  });
});
