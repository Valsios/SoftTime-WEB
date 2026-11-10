import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiService } from '../../core/api';
import { MlHsAccuracy, MlHsForecast, MlHsWeek, MlJobState, MlStatus } from '../models';

/** Moteur prédictif : état, prévisions et déclenchement des traitements. */
@Injectable({ providedIn: 'root' })
export class PredictionsService extends ApiService {
  status(): Observable<MlStatus> {
    return this.http.get<MlStatus>(this.url('/api/v1/predictions/status'));
  }

  job(): Observable<MlJobState> {
    return this.http.get<MlJobState>(this.url('/api/v1/predictions/job'));
  }

  overtime(week?: string): Observable<MlHsForecast[]> {
    return this.http.get<MlHsForecast[]>(this.url('/api/v1/predictions/overtime'), {
      params: this.params({ week }),
    });
  }

  /** Totaux HS prévus par semaine sur l'horizon (semaine en cours comprise). */
  horizon(): Observable<MlHsWeek[]> {
    return this.http.get<MlHsWeek[]>(this.url('/api/v1/predictions/overtime/horizon'));
  }

  /** Prévu / réel des dernières semaines terminées. */
  accuracy(weeks = 8): Observable<MlHsAccuracy[]> {
    return this.http.get<MlHsAccuracy[]>(this.url('/api/v1/predictions/overtime/accuracy'), {
      params: this.params({ weeks }),
    });
  }

  refresh(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(this.url('/api/v1/predictions/refresh'), {});
  }

  train(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(this.url('/api/v1/predictions/train'), {});
  }

  backfill(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(this.url('/api/v1/predictions/backfill'), {});
  }
}
