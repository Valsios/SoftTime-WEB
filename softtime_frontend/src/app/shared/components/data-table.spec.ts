import { TestBed } from '@angular/core/testing';
import { afterEach, describe, expect, it } from 'vitest';
import { DataTable } from './data-table';

function creer(inputs: Record<string, unknown> = {}) {
  TestBed.resetTestingModule();
  const fixture = TestBed.createComponent(DataTable);
  fixture.componentRef.setInput('columns', [{ key: 'nom', label: 'Nom' }]);
  fixture.componentRef.setInput('rows', [{ id: 1, nom: 'A' }]);
  for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
  fixture.detectChanges();
  return fixture.nativeElement as HTMLElement;
}

describe('DataTable : légende (caption)', () => {
  afterEach(() => TestBed.resetTestingModule());

  for (const [mode, inputs] of [
    ['client', {}],
    ['serveur', { serverSide: true, total: 1 }],
  ] as const) {
    it(`mode ${mode} : avec caption, un <caption> est présent et masqué visuellement`, () => {
      const el = creer({ ...inputs, caption: 'Anomalies de la période' });
      const caption = el.querySelector('table.soft-table > caption') as HTMLElement | null;
      expect(caption).not.toBeNull();
      expect(caption!.textContent?.trim()).toBe('Anomalies de la période');
      expect(caption!.style.position).toBe('absolute');
      expect(caption!.style.width).toBe('1px');
      expect(caption!.style.overflow).toBe('hidden');
    });

    it(`mode ${mode} : sans caption, aucun <caption> (DOM inchangé)`, () => {
      expect(creer(inputs).querySelector('caption')).toBeNull();
    });

    it(`mode ${mode} : caption vide, aucun <caption>`, () => {
      expect(creer({ ...inputs, caption: '' }).querySelector('caption')).toBeNull();
    });
  }
});
