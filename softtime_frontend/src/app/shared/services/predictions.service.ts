import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import { MlAbsenceForecast, MlHsForecast, MlJobState, MlStatus } from '../models';

export interface MlAbsenceQuery {
  from?: string;
  to?: string;
  departement?: string | null;
  categorie?: number | null;
}

/** Moteur prédictif : état, prévisions et déclenchement des traitements. */
@Injectable({ providedIn: 'root' })
export class PredictionsService extends ApiService {
  status(): Observable<MlStatus> {
    return this.http.get<MlStatus>(this.url('/api/predictions/status'));
  }

  job(): Observable<MlJobState> {
    return this.http.get<MlJobState>(this.url('/api/predictions/job'));
  }

  absence(query: MlAbsenceQuery = {}): Observable<MlAbsenceForecast[]> {
    return this.http.get<MlAbsenceForecast[]>(this.url('/api/predictions/absence'), {
      params: this.params({ ...query }),
    });
  }

  overtime(week?: string): Observable<MlHsForecast[]> {
    return this.http.get<MlHsForecast[]>(this.url('/api/predictions/overtime'), {
      params: this.params({ week }),
    });
  }

  refresh(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(this.url('/api/predictions/refresh'), {});
  }

  train(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(this.url('/api/predictions/train'), {});
  }

  backfill(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(this.url('/api/predictions/backfill'), {});
  }
}
