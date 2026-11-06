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

describe('PeriodFilter : busy et changed (ajouts optionnels)', () => {
  afterEach(() => TestBed.resetTestingModule());

  const bouton = (fixture: { nativeElement: HTMLElement }) => fixture.nativeElement.querySelector('button.soft-btn') as HTMLButtonElement;

  it('par défaut : bouton actif, sans état occupé, comme avant', () => {
    const { fixture, filtre } = creer();
    expect(filtre.busy()).toBe(false);
    expect(bouton(fixture).disabled).toBe(false);
    expect(fixture.nativeElement.querySelector('soft-button')?.hasAttribute('aria-busy')).toBe(false);
  });

  it('busy = true : bouton désactivé avec son indicateur, aucune émission au clic', () => {
    const { fixture, emissions } = creer({ busy: true });
    expect(bouton(fixture).disabled).toBe(true);
    expect(bouton(fixture).querySelector('.soft-btn__spinner')).not.toBeNull();
    expect(fixture.nativeElement.querySelector('soft-button')?.getAttribute('aria-busy')).toBe('true');
    bouton(fixture).click();
    expect(emissions).toHaveLength(0);
  });

  it('changed : émise quand l’utilisateur modifie un champ, jamais à l’affichage ni au remplissage par lien profond', async () => {
    const { fixture, filtre } = creer({ initial: { from: '2025-11-12', to: '2025-11-13', matricule: null } });
    let n = 0;
    filtre.changed.subscribe(() => n++);
    await fixture.whenStable();
    fixture.detectChanges();
    expect(n).toBe(0);

    const debut = fixture.nativeElement.querySelector('input[name="from"]') as HTMLInputElement;
    debut.value = '2025-11-01';
    debut.dispatchEvent(new Event('input', { bubbles: true }));
    expect(n).toBe(1);
    expect(filtre.from).toBe('2025-11-01');
  });
});
