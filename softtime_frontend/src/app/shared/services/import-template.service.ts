import { Injectable } from '@angular/core';
import { Observable, shareReplay } from 'rxjs';
import { ApiService } from '../../core/api';
import { ImportTemplate } from '../models';

/**
 * Modèles d'import exposés par le backend : matrice des colonnes, exemples et fichier
 * téléchargeable (les définitions sont alignées sur les parseurs d'import).
 */
@Injectable({ providedIn: 'root' })
export class ImportTemplateService extends ApiService {
  private cache$?: Observable<ImportTemplate[]>;

  /** Liste complète, chargée une seule fois par session. */
  list(): Observable<ImportTemplate[]> {
    return (this.cache$ ??= this.http
      .get<ImportTemplate[]>(this.url('/api/import-templates'))
      .pipe(shareReplay(1)));
  }

  /** Télécharge le fichier modèle (.xlsx ou .csv) d'une clé du catalogue. */
  download(template: ImportTemplate): Observable<Blob> {
    return this.http.get(this.url(`/api/import-templates/${encodeURIComponent(template.key)}`), {
      responseType: 'blob',
    });
  }
}
