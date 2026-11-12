import { TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { Droit } from '../../core/rights';
import { SessionStore } from '../../core/session.store';
import { PeriodRequest } from '../models';
import { CardPaieService, DepartementServiceService } from '../services/catalog.service';
import { PeriodFilter } from './period-filter';

/** Cartes sans département : le filtre voudrait compléter par la recherche SAGE (matricule → département). */
const cartesSansDepartement = [
  { sageMatricule: '02417', sageNom: 'A', sagePrenom: 'B', departement: null },
  { sageMatricule: '04171', sageNom: 'C', sagePrenom: 'D', departement: null },
];

interface Compte {
  gates: number[];
  permissions?: string[];
}

function creer(compte: Compte, liste: () => Observable<unknown[]> = () => of(cartesSansDepartement), inputs: Record<string, unknown> = {}) {
  const list = vi.fn(liste);
  const lookupBatch = vi.fn(() => of([{ matricule: '02417', departement: 'D9' }]));
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      { provide: CardPaieService, useValue: { list } },
      { provide: DepartementServiceService, useValue: { lookupBatch } },
      {
        provide: SessionStore,
        useValue: {
          hasRight: (d: number) => compte.gates.includes(d),
          hasPermission: (code: string, d: number) => compte.gates.includes(d) && (compte.permissions ?? []).includes(code),
        },
      },
    ],
  });
  const fixture = TestBed.createComponent(PeriodFilter);
  for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
  const emissions: PeriodRequest[] = [];
  fixture.componentInstance.search.subscribe((r) => emissions.push(r));
  fixture.detectChanges();
  return { filtre: fixture.componentInstance, list, lookupBatch, emissions };
}

describe('PeriodFilter : listes lues selon les portes du compte', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('compte sans aucune porte 2 à 6 : aucun appel à card-paie ni à la recherche SAGE', () => {
    const { list, lookupBatch, filtre } = creer({ gates: [Droit.Databases] });
    expect(list).not.toHaveBeenCalled();
    expect(lookupBatch).not.toHaveBeenCalled();
    expect(filtre.matriculeOptions()).toEqual([]);
  });

  it('compte sans porte 2 à 6 avec lien profond : la recherche part quand même, sans matricule', () => {
    const { emissions } = creer({ gates: [] }, undefined, {
      initial: { from: '2025-11-12', to: '2025-11-12', matricule: '4171' },
    });
    expect(emissions).toHaveLength(1);
    expect(emissions[0].matriculeFrom).toBeNull();
  });

  it.each([Droit.Parameters, Droit.Processing, Droit.Other, Droit.Traceability, Droit.Reports])(
    'porte %i seule : la liste des matricules est lue (en silence)',
    (gate) => {
      const { list, filtre } = creer({ gates: [gate] });
      expect(list).toHaveBeenCalledExactlyOnceWith({ silent: true });
      expect(filtre.matriculeOptions().map((o) => o.value)).toEqual(['02417', '04171']);
    },
  );

  it('portes 3, 4, 5 et 6 sans PARAMS:CARDPAIE : la recherche SAGE (donnée personnelle, porte 2) n’est jamais appelée', () => {
    const { lookupBatch, filtre } = creer({ gates: [3, 4, 5, 6], permissions: ['PROC:CORRECTED_HOURS'] });
    expect(lookupBatch).not.toHaveBeenCalled();
    expect(filtre.departementOptions()).toEqual([]);
  });

  it('porte 2 avec PARAMS:CARDPAIE : la recherche SAGE complète les départements comme avant', () => {
    const { lookupBatch, filtre } = creer({ gates: [Droit.Parameters], permissions: ['PARAMS:CARDPAIE'] });
    expect(lookupBatch).toHaveBeenCalledWith(['02417', '04171']);
    expect(filtre.departementOptions()).toEqual([{ value: 'D9', label: 'D9' }]);
  });

  it('échec de la lecture (403) : listes vides, aucune erreur levée, recherche toujours possible', () => {
    const { filtre, emissions } = creer({ gates: [Droit.Processing] }, () => throwError(() => ({ status: 403 })));
    expect(filtre.matriculeOptions()).toEqual([]);
    expect(filtre.departementOptions()).toEqual([]);
    filtre.emit();
    expect(emissions).toHaveLength(1);
  });
});
