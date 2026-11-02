import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { afterEach, describe, expect, it } from 'vitest';
import { PeriodRequest } from '../models';
import { CardPaieService, DepartementServiceService } from '../services/catalog.service';
import { PeriodFilter } from './period-filter';

const cartes = [
  { sageMatricule: '02417', sageNom: 'A', sagePrenom: 'B', departement: 'D1' },
  { sageMatricule: '04171', sageNom: 'C', sagePrenom: 'D', departement: 'D1' },
];

function creer(inputs: Record<string, unknown> = {}) {
  TestBed.resetTestingModule();
  TestBed.configureTestingModule({
    providers: [
      { provide: CardPaieService, useValue: { list: () => of(cartes) } },
      { provide: DepartementServiceService, useValue: { lookupBatch: () => of([]) } },
    ],
  });
  const fixture = TestBed.createComponent(PeriodFilter);
  for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
  const emissions: PeriodRequest[] = [];
  fixture.componentInstance.search.subscribe((r) => emissions.push(r));
  fixture.detectChanges();
  return { fixture, filtre: fixture.componentInstance, emissions };
}

describe('PeriodFilter : lien profond', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('initial = null : état identique à l’actuel (période par défaut, aucune émission, aucun matricule)', () => {
    const { filtre, emissions } = creer();
    expect(emissions).toHaveLength(0);
    expect(filtre.matriculeFrom).toBeNull();
    expect(filtre.matriculeTo).toBeNull();
    expect(filtre.initial()).toBeNull();
  });

  it('autoSearch seul reste inchangé : une émission, sans matricule', () => {
    const { emissions } = creer({ autoSearch: true });
    expect(emissions).toHaveLength(1);
    expect(emissions[0].matriculeFrom).toBeNull();
  });

  it('initial valide : champs remplis (4171 retrouvé comme 04171) et une seule émission', () => {
    const { filtre, emissions } = creer({ initial: { from: '2025-11-12', to: '2025-11-13', matricule: '4171' } });
    expect(filtre.from).toBe('2025-11-12');
    expect(filtre.to).toBe('2025-11-13');
    expect(emissions).toEqual([
      { from: '2025-11-12', to: '2025-11-13', matriculeFrom: '04171', matriculeTo: '04171', departement: null },
    ]);
  });

  it('initial sans matricule : une émission immédiate sans filtre matricule', () => {
    const { emissions } = creer({ initial: { from: '2025-11-12', to: '2025-11-12', matricule: null } });
    expect(emissions).toHaveLength(1);
    expect(emissions[0].matriculeFrom).toBeNull();
  });

  it('matricule sans correspondance : recherche sur la période, sans filtre matricule', () => {
    const { emissions } = creer({ initial: { from: '2025-11-12', to: '2025-11-12', matricule: '9999' } });
    expect(emissions).toHaveLength(1);
    expect(emissions[0].matriculeFrom).toBeNull();
  });

  it('sans liste de matricules affichée : le matricule n’est émis qu’une fois', () => {
    const { filtre, emissions } = creer({ showMatricule: false, initial: { from: '2025-11-12', to: '2025-11-12', matricule: '4171' } });
    expect(emissions).toHaveLength(1);
    expect(emissions[0].matriculeFrom).toBe('04171');
    filtre.emit();
    expect(emissions[1].matriculeFrom).toBeNull();
  });

  it('second lien vers la même page : nouveau remplissage et nouvelle émission', () => {
    const { fixture, filtre, emissions } = creer({ initial: { from: '2025-11-12', to: '2025-11-12', matricule: '4171' } });
    fixture.componentRef.setInput('initial', { from: '2025-11-20', to: '2025-11-21', matricule: '2417' });
    fixture.detectChanges();
    expect(filtre.from).toBe('2025-11-20');
    expect(emissions).toHaveLength(2);
    expect(emissions[1].matriculeFrom).toBe('02417');
  });
});
