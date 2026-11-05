import { describe, expect, it } from 'vitest';
import { DEEP_LINK_MAX_DAYS, matchMatricule, matriculeKey, readDeepLink } from './deep-link';

describe('readDeepLink', () => {
  it('paramètres absents ou vides : null', () => {
    expect(readDeepLink(null)).toBeNull();
    expect(readDeepLink(undefined)).toBeNull();
    expect(readDeepLink({})).toBeNull();
    expect(readDeepLink({ du: '', au: '', matricule: '' })).toBeNull();
  });

  it('paramètres valides', () => {
    expect(readDeepLink({ du: '2025-11-12', au: '2025-11-13', matricule: '04171' }))
      .toEqual({ from: '2025-11-12', to: '2025-11-13', matricule: '04171' });
    expect(readDeepLink({ du: '2025-11-12', au: '2025-11-12' })?.matricule).toBeNull();
  });

  it('date invalide ou non réelle : null', () => {
    expect(readDeepLink({ du: '2025-02-31', au: '2025-03-01' })).toBeNull();
    expect(readDeepLink({ du: '12/11/2025', au: '2025-11-13' })).toBeNull();
    expect(readDeepLink({ du: '2025-11-12T00:00:00', au: '2025-11-13' })).toBeNull();
    expect(readDeepLink({ du: '2025-13-01', au: '2025-13-02' })).toBeNull();
  });

  it('du après au : null', () => {
    expect(readDeepLink({ du: '2025-11-13', au: '2025-11-12' })).toBeNull();
  });

  it('plage trop longue : null ; la limite exacte est acceptée', () => {
    expect(DEEP_LINK_MAX_DAYS).toBe(31);
    expect(readDeepLink({ du: '2025-11-01', au: '2025-12-02' })).not.toBeNull();
    expect(readDeepLink({ du: '2025-11-01', au: '2025-12-03' })).toBeNull();
  });

  it('fin de mois et d’année', () => {
    expect(readDeepLink({ du: '2025-12-31', au: '2026-01-01' })).not.toBeNull();
    expect(readDeepLink({ du: '2024-02-29', au: '2024-02-29' })).not.toBeNull();
    expect(readDeepLink({ du: '2025-02-29', au: '2025-02-29' })).toBeNull();
  });

  it('matricule dangereux, trop long ou mal formé : ignoré, la période reste valide', () => {
    const base = { du: '2025-11-12', au: '2025-11-12' };
    expect(readDeepLink({ ...base, matricule: '<script>alert(1)</script>' })?.matricule).toBeNull();
    expect(readDeepLink({ ...base, matricule: '04 171' })?.matricule).toBeNull();
    expect(readDeepLink({ ...base, matricule: '12345678901' })?.matricule).toBeNull();
    expect(readDeepLink({ ...base, matricule: "1' OR '1'='1" })?.matricule).toBeNull();
    expect(readDeepLink({ ...base, matricule: '   ' })?.matricule).toBeNull();
  });

  it('matricule : espaces autour retirés, dix caractères acceptés', () => {
    const base = { du: '2025-11-12', au: '2025-11-12' };
    expect(readDeepLink({ ...base, matricule: ' 04171 ' })?.matricule).toBe('04171');
    expect(readDeepLink({ ...base, matricule: 'ABCDEFGHI0' })?.matricule).toBe('ABCDEFGHI0');
  });

  it('valeurs en tableau ou autres types : ignorées', () => {
    expect(readDeepLink({ du: ['2025-11-12'], au: '2025-11-12' })).toBeNull();
    expect(readDeepLink({ du: '2025-11-12', au: ['2025-11-12', '2025-11-13'] })).toBeNull();
    expect(readDeepLink({ du: 20251112, au: '2025-11-12' })).toBeNull();
    expect(readDeepLink({ du: '2025-11-12', au: '2025-11-12', matricule: ['04171'] })?.matricule).toBeNull();
    expect(readDeepLink({ du: '2025-11-12', au: '2025-11-12', matricule: 4171 })?.matricule).toBeNull();
  });
});

describe('matchMatricule', () => {
  const options = [{ value: '02417' }, { value: '04171' }, { value: 'a12' }, { value: '14171' }];

  it('4171 <-> 04171 : la valeur de l’option est renvoyée', () => {
    expect(matchMatricule('4171', options)).toBe('04171');
    expect(matchMatricule('04171', [{ value: '4171' }])).toBe('4171');
    expect(matchMatricule('04171', options)).toBe('04171');
  });

  it('alphanumérique : majuscules', () => {
    expect(matchMatricule('A12', options)).toBe('a12');
    expect(matchMatricule('a12', [{ value: 'A12' }])).toBe('A12');
  });

  it('aucune option correspondante ou liste vide : null', () => {
    expect(matchMatricule('9999', options)).toBeNull();
    expect(matchMatricule('4171', [])).toBeNull();
    expect(matchMatricule('', options)).toBeNull();
  });

  it('même règle que MatriculeIdentity.Cle', () => {
    expect(matriculeKey('00000')).toBe('0');
    expect(matriculeKey(' 02417 ')).toBe('2417');
    expect(matriculeKey('ab01')).toBe('AB01');
  });
});
