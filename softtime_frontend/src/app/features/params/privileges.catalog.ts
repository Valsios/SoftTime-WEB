import { CatalogDomain } from '../../shared/models';

export interface CatalogItem {
  code: string;
  label: string;
  canCreate?: boolean;
  canUpdate?: boolean;
  canDelete?: boolean;
  desc?: string;
}

export interface CatalogCategory {
  key: string;
  title: string;
  icon: string;
  items: CatalogItem[];
}

/**
 * Catégories de l'écran Privilèges construites depuis le catalogue du serveur (source unique des codes) : une page par
 * code « DOMAINE:PAGE », ses actions (CREATE, UPDATE, DELETE) d'après les codes « DOMAINE:PAGE:ACTION » présents.
 * Libellés, titres et icônes viennent de la liste locale ; un code ou un domaine inconnu localement s'affiche par son
 * code. Un catalogue vide ou illisible laisse la liste locale inchangée (l'écran ne casse jamais).
 */
export function categoriesFromCatalog(domains: readonly CatalogDomain[] | null | undefined, local: readonly CatalogCategory[]): CatalogCategory[] {
  if (!Array.isArray(domains) || domains.length === 0) return [...local];
  const categories = domains
    .filter((d) => d && typeof d.key === 'string' && Array.isArray(d.codes))
    .map((domain) => toCategory(domain, local.find((c) => c.key === domain.key)))
    .filter((c) => c.items.length > 0);
  return categories.length > 0 ? categories : [...local];
}

function toCategory(domain: CatalogDomain, local: CatalogCategory | undefined): CatalogCategory {
  const codes = new Set(domain.codes);
  const pages = domain.codes.filter((code) => code.split(':').length === 2);
  return {
    key: domain.key,
    title: local?.title ?? domain.key,
    icon: local?.icon ?? 'grid',
    items: pages.map((page) => {
      const known = local?.items.find((i) => i.code === page);
      return {
        code: page,
        label: known?.label ?? page,
        ...(known?.desc ? { desc: known.desc } : {}),
        ...(codes.has(`${page}:CREATE`) ? { canCreate: true } : {}),
        ...(codes.has(`${page}:UPDATE`) ? { canUpdate: true } : {}),
        ...(codes.has(`${page}:DELETE`) ? { canDelete: true } : {}),
      };
    }),
  };
}
