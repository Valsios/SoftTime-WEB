import type { CardPaie } from '../models';

export interface EmployeeLabelSource {
  matricule?: string | null;
  nom?: string | null;
  prenom?: string | null;
  fallbackId?: number | null;
}

/**
 * Libellé lisible d'un salarié : « MATRICULE — NOM Prénom ».
 * L'identifiant technique n'est utilisé qu'en dernier recours, quand aucun
 * matricule ni nom n'est disponible.
 */
export function employeeLabel(source: EmployeeLabelSource): string {
  const matricule = (source.matricule ?? '').trim();
  const name = [source.nom, source.prenom]
    .map((part) => (part ?? '').trim())
    .filter(Boolean)
    .join(' ');

  if (matricule && name) return `${matricule} — ${name}`;
  if (matricule) return matricule;
  if (name) return name;
  return source.fallbackId ? `Salarié #${source.fallbackId}` : 'Salarié';
}

/** Libellé d'une carte paie dans les listes de sélection. */
export function cardEmployeeLabel(
  card: Pick<CardPaie, 'id' | 'sageMatricule' | 'sageNom' | 'sagePrenom'>,
): string {
  return employeeLabel({
    matricule: card.sageMatricule,
    nom: card.sageNom,
    prenom: card.sagePrenom,
    fallbackId: card.id,
  });
}
