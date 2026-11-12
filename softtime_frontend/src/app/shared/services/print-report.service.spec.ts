import { TestBed } from '@angular/core/testing';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { PrintReportService } from './print-report.service';

/**
 * Impression d'un rapport : comportement repris À L'IDENTIQUE de l'ancien export PDF du tableau de
 * bord analytique (titre = nom de fichier, thème clair forcé, restauration à afterprint ou en dernier
 * recours, restauration immédiate si l'impression échoue).
 */
describe('PrintReportService', () => {
  const root = document.documentElement;
  let print: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    vi.useFakeTimers();
    document.title = 'Soft Time';
    root.setAttribute('data-theme', 'dark');
    print = vi.spyOn(window, 'print').mockImplementation(() => undefined);
  });

  afterEach(() => {
    print.mockRestore();
    vi.useRealTimers();
    root.removeAttribute('data-theme');
    TestBed.resetTestingModule();
  });

  function session() {
    const etats: { title: string; theme: string | null; moment: string }[] = [];
    return {
      etats,
      s: {
        title: 'rapport_2025-11-01_2025-11-30',
        onStart: vi.fn(() => etats.push({ title: document.title, theme: root.getAttribute('data-theme'), moment: 'start' })),
        onEnd: vi.fn(() => etats.push({ title: document.title, theme: root.getAttribute('data-theme'), moment: 'end' })),
      },
    };
  }

  it('avant la boîte d’impression : titre = nom de fichier, thème clair, écran en mode impression', () => {
    const { s, etats } = session();
    TestBed.inject(PrintReportService).print(s);

    expect(print).toHaveBeenCalledTimes(1);
    expect(etats[0]).toEqual({ title: 'rapport_2025-11-01_2025-11-30', theme: 'light', moment: 'start' });
    expect(s.onEnd).not.toHaveBeenCalled();
  });

  it('à la fermeture (afterprint) : titre et thème d’origine restaurés, onEnd appelé une seule fois', () => {
    const { s, etats } = session();
    TestBed.inject(PrintReportService).print(s);

    window.dispatchEvent(new Event('afterprint'));
    vi.advanceTimersByTime(PrintReportService.FALLBACK_MS);

    expect(s.onEnd).toHaveBeenCalledTimes(1);
    expect(etats[1]).toEqual({ title: 'Soft Time', theme: 'dark', moment: 'end' });
  });

  it('sans afterprint : restauration en dernier recours après 5 minutes', () => {
    const { s } = session();
    TestBed.inject(PrintReportService).print(s);

    vi.advanceTimersByTime(PrintReportService.FALLBACK_MS - 1);
    expect(s.onEnd).not.toHaveBeenCalled();
    vi.advanceTimersByTime(1);
    expect(s.onEnd).toHaveBeenCalledTimes(1);
    expect(root.getAttribute('data-theme')).toBe('dark');
  });

  it('thème d’origine absent : l’attribut est retiré, pas laissé à « light »', () => {
    root.removeAttribute('data-theme');
    const { s } = session();
    TestBed.inject(PrintReportService).print(s);

    window.dispatchEvent(new Event('afterprint'));
    expect(root.hasAttribute('data-theme')).toBe(false);
  });

  it('impression impossible : restauration immédiate', () => {
    print.mockImplementation(() => {
      throw new Error('bloqué');
    });
    const { s } = session();
    TestBed.inject(PrintReportService).print(s);

    expect(s.onEnd).toHaveBeenCalledTimes(1);
    expect(document.title).toBe('Soft Time');
    expect(root.getAttribute('data-theme')).toBe('dark');
  });
});
