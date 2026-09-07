/** Mirrors SoftTime.Domain.Rights (backend). */
export const Droit = {
  Databases: 1,
  Parameters: 2,
  Processing: 3,
  Other: 4,
  Traceability: 5,
  Reports: 6,
} as const;

export type DroitValue = (typeof Droit)[keyof typeof Droit];
