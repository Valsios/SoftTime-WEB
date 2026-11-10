import { describe, expect, it } from 'vitest';
import { isApiEnvelope, unwrapApiBody } from './api-envelope';

describe('api-envelope', () => {
  it('reconnaît une enveloppe de succès et en extrait les données', () => {
    const body = { success: true, message: 'OK', data: { id: 1 }, timestamp: '2026-01-01T00:00:00Z' };
    expect(isApiEnvelope(body)).toBe(true);
    expect(unwrapApiBody(body)).toEqual({ id: 1 });
  });

  it('laisse intacts les corps qui ne sont pas des enveloppes', () => {
    expect(isApiEnvelope([1, 2])).toBe(false);
    expect(unwrapApiBody({ error: 'x' })).toEqual({ error: 'x' });
    expect(unwrapApiBody('texte')).toBe('texte');
  });

  it('renvoie null pour une enveloppe sans données', () => {
    expect(unwrapApiBody({ success: false, message: 'Refusé', timestamp: 't' })).toBeNull();
  });
});
