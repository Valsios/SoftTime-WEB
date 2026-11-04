import { describe, expect, it } from 'vitest';
import { shouldShowToast, TOAST_DEDUPE_WINDOW_MS } from './toast.logic';

describe('toast.logic', () => {
  it('un message d’erreur identique dans la fenêtre de 3 s n’est affiché qu’une fois', () => {
    const seen = new Map<string, number>();
    expect(shouldShowToast(seen, 'error', 'Accès refusé', 1000)).toBe(true);
    expect(shouldShowToast(seen, 'error', 'Accès refusé', 1500)).toBe(false);
    expect(shouldShowToast(seen, 'error', 'Accès refusé', 3999)).toBe(false);
  });

  it('après la fenêtre, le même message est de nouveau affiché', () => {
    const seen = new Map<string, number>();
    expect(shouldShowToast(seen, 'error', 'Accès refusé', 0)).toBe(true);
    expect(shouldShowToast(seen, 'error', 'Accès refusé', TOAST_DEDUPE_WINDOW_MS)).toBe(true);
  });

  it('des messages différents sont tous affichés', () => {
    const seen = new Map<string, number>();
    expect(shouldShowToast(seen, 'error', 'A', 0)).toBe(true);
    expect(shouldShowToast(seen, 'error', 'B', 10)).toBe(true);
  });

  it('seules les erreurs sont dédupliquées', () => {
    const seen = new Map<string, number>();
    expect(shouldShowToast(seen, 'success', 'Enregistré.', 0)).toBe(true);
    expect(shouldShowToast(seen, 'success', 'Enregistré.', 10)).toBe(true);
    expect(shouldShowToast(seen, 'info', 'Info', 0)).toBe(true);
    expect(shouldShowToast(seen, 'info', 'Info', 10)).toBe(true);
  });

  it('la mémoire des messages anciens est purgée', () => {
    const seen = new Map<string, number>();
    shouldShowToast(seen, 'error', 'ancien', 0);
    shouldShowToast(seen, 'error', 'récent', 10_000);
    expect(seen.has('ancien')).toBe(false);
  });
});
