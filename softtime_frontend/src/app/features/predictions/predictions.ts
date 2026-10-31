import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import {
  MlAbsenceForecast,
  MlHsForecast,
  MlJobState,
  MlModelInfo,
  MlStatus,
} from '../../shared/models';
import { PredictionsService } from '../../shared/services/predictions.service';
import { PageHeader, SoftBadge, SoftButton, SoftCard } from '../../shared/components';
import { fmtDate as formatIsoDate } from '../../shared/utils/date';

/**
 * Moteur prédictif : état des modèles, risque d'absence sur les prochains jours et heures
 * supplémentaires prévues. Les prédictions sont précalculées côté serveur ; cette page ne fait
 * que les filtrer et les présenter.
 */
@Component({
  selector: 'app-predictions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, SoftCard, SoftButton, SoftBadge],
  template: `
    <page-header
      title="Prévisions"
      subtitle="Absentéisme et heures supplémentaires estimés par le moteur ML.NET"
    >
      <soft-button variant="secondary" (click)="load()" [disabled]="loading()">Actualiser</soft-button>
      <soft-button variant="secondary" (click)="action('refresh')" [disabled]="job()?.running ?? false">
        Rafraîchir les données
      </soft-button>
      <soft-button variant="secondary" (click)="action('train')" [disabled]="job()?.running ?? false">
        Réentraîner
      </soft-button>
      <soft-button variant="ghost" (click)="action('backfill')" [disabled]="job()?.running ?? false">
        Reconstruire l'historique
      </soft-button>
    </page-header>

    @if (error()) {
      <div class="pred-alert pred-alert--error" role="alert">{{ error() }}</div>
    }
    @if (info()) {
      <div class="pred-alert pred-alert--info" role="status">{{ info() }}</div>
    }

    <div class="pred-status">
      <span class="pred-status__dot" [class]="'pred-status__dot pred-status__dot--' + etatClass()"></span>
      <div class="pred-status__text">
        <strong>{{ etatLabel() }}</strong>
        <span>{{ status()?.message || 'Chargement…' }}</span>
      </div>
      @if (status()?.derniereGeneration) {
        <span class="pred-status__meta">Dernière génération : {{ fmtDate(status()?.derniereGeneration) }}</span>
      }
      @if (job()?.running) {
        <soft-badge variant="warning">Traitement en cours…</soft-badge>
      }
    </div>

    @if (loading() && !status()) {
      <div class="pred-empty">Chargement des prévisions…</div>
    } @else if (!status()?.enabled) {
      <div class="pred-empty">
        Le moteur est désactivé (Ml:Enabled). Activez-le dans appsettings.json pour générer des prévisions.
      </div>
    } @else if (status()?.etat === 'Baseline') {
      <div class="pred-empty">
        Aucun modèle actif pour cette société : les données sont insuffisantes ou le modèle ne bat pas
        encore la baseline. Lancez « Reconstruire l'historique » puis « Réentraîner ».
      </div>
    } @else {
      <div class="pred-kpis">
        <soft-card variant="bordered">
          <div class="pred-kpi">
            <span>Salariés à risque élevé</span>
            <strong>{{ salariesARisque() }}</strong>
          </div>
        </soft-card>
        <soft-card variant="bordered">
          <div class="pred-kpi">
            <span>Risque moyen (14 j)</span>
            <strong>{{ pct(risqueMoyen()) }}</strong>
          </div>
        </soft-card>
        <soft-card variant="bordered">
          <div class="pred-kpi">
            <span>HS prévues (semaine)</span>
            <strong>{{ hsTotal().toFixed(1) }} h</strong>
          </div>
        </soft-card>
        <soft-card variant="bordered">
          <div class="pred-kpi">
            <span>Qualité du modèle d'absence</span>
            <strong>{{ absenceQualite() }}</strong>
          </div>
        </soft-card>
      </div>

      <soft-card variant="bordered" padding="sm">
        <div class="pred-section-head">
          <h2>Absences prévues</h2>
          <span>{{ absences().length }} salarié(s) · horizon {{ horizonJours }} jours</span>
        </div>
        @if (!absences().length) {
          <div class="pred-empty">Aucune prévision d'absence sur l'horizon.</div>
        } @else {
          <div class="soft-table-wrap">
            <table class="soft-table pred-table">
              <thead>
                <tr>
                  <th>Salarié</th>
                  <th>Département</th>
                  <th>Risque</th>
                  <th>Jours à risque</th>
                </tr>
              </thead>
              <tbody>
                @for (a of absences(); track a.matricule) {
                  <tr>
                    <td>
                      <strong>{{ a.nom }} {{ a.prenom }}</strong>
                      <small>{{ a.matricule }}</small>
                    </td>
                    <td>{{ a.departement || '—' }}</td>
                    <td>
                      <soft-badge [variant]="niveauVariant(a.niveau)">{{ pct(a.risque) }}</soft-badge>
                    </td>
                    <td>
                      <span class="pred-days">
                        @for (j of a.jours; track j.date) {
                          <span class="pred-day" [class]="'pred-day pred-day--' + niveauClass(j.niveau)">
                            {{ jourCourt(j.date) }} · {{ pct(j.probabilite) }}
                          </span>
                        }
                      </span>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </soft-card>

      <soft-card variant="bordered" padding="sm">
        <div class="pred-section-head">
          <h2>Heures supplémentaires prévues</h2>
          <span>{{ semaineLabel() }}</span>
        </div>
        @if (!hs().length) {
          <div class="pred-empty">Aucune prévision HS : planning de la semaine incomplet ou modèle indisponible.</div>
        } @else {
          <div class="soft-table-wrap">
            <table class="soft-table pred-table">
              <thead>
                <tr>
                  <th>Salarié</th>
                  <th>Département</th>
                  <th>HS prévues</th>
                  <th>Intervalle</th>
                  <th>P(dépassement)</th>
                </tr>
              </thead>
              <tbody>
                @for (h of hs(); track h.matricule) {
                  <tr>
                    <td>
                      <strong>{{ h.nom }} {{ h.prenom }}</strong>
                      <small>{{ h.matricule }}</small>
                    </td>
                    <td>{{ h.departement || '—' }}</td>
                    <td><strong>{{ h.heuresPrevues.toFixed(1) }} h</strong></td>
                    <td>{{ h.borneBasse.toFixed(1) }} – {{ h.borneHaute.toFixed(1) }} h</td>
                    <td>
                      <soft-badge [variant]="niveauVariant(h.niveau)">{{ pct(h.probabiliteDepassement) }}</soft-badge>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          </div>
        }
      </soft-card>
    }

    <soft-card variant="bordered" padding="sm">
      <div class="pred-section-head">
        <h2>Modèles</h2>
        <span>Registre versionné des entraînements</span>
      </div>
      @if (!modeles().length) {
        <div class="pred-empty">Aucun entraînement enregistré.</div>
      } @else {
        <div class="soft-table-wrap">
          <table class="soft-table pred-table">
            <thead>
              <tr>
                <th>Type</th>
                <th>Version</th>
                <th>Algorithme</th>
                <th>Statut</th>
                <th>Entraîné le</th>
                <th>Métriques</th>
                <th>Commentaire</th>
              </tr>
            </thead>
            <tbody>
              @for (m of modeles(); track m.id) {
                <tr>
                  <td>{{ typeLabel(m.typeModele) }}</td>
                  <td>{{ m.version }}</td>
                  <td>{{ m.algorithme }}</td>
                  <td>
                    <soft-badge [variant]="statutVariant(m)">{{ m.statut }}</soft-badge>
                  </td>
                  <td>{{ fmtDate(m.entraineLe) }}</td>
                  <td>{{ metricLabel(m) }}</td>
                  <td>{{ m.commentaire || '—' }}</td>
                </tr>
              }
            </tbody>
          </table>
        </div>
      }
    </soft-card>
  `,
  styles: [
    `
      :host {
        display: flex;
        flex-direction: column;
        gap: 16px;
      }
      .pred-alert {
        border-radius: var(--radius-md);
        padding: 10px 14px;
        font-size: 13px;
      }
      .pred-alert--error {
        background: var(--danger-50, #fef2f2);
        color: var(--danger-700, #b91c1c);
        border: 1px solid var(--danger-200, #fecaca);
      }
      .pred-alert--info {
        background: var(--info-50, #eff6ff);
        color: var(--info-700, #1d4ed8);
        border: 1px solid var(--info-200, #bfdbfe);
      }
      .pred-status {
        display: flex;
        align-items: center;
        gap: 10px;
        padding: 12px 16px;
        border: 1px solid var(--app-border);
        border-radius: var(--radius-md);
        background: var(--app-surface);
        flex-wrap: wrap;
      }
      .pred-status__dot {
        width: 10px;
        height: 10px;
        border-radius: 50%;
        flex: none;
      }
      .pred-status__dot--pret {
        background: var(--success-500, #22c55e);
      }
      .pred-status__dot--baseline {
        background: var(--warning-500, #f59e0b);
      }
      .pred-status__dot--desactive {
        background: var(--text-muted, #94a3b8);
      }
      .pred-status__text {
        display: flex;
        flex-direction: column;
        gap: 2px;
        min-width: 240px;
      }
      .pred-status__text strong {
        font-size: 13.5px;
      }
      .pred-status__text span,
      .pred-status__meta {
        font-size: 12.5px;
        color: var(--text-muted);
      }
      .pred-status__meta {
        margin-left: auto;
      }
      .pred-kpis {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
        gap: 12px;
      }
      .pred-kpi {
        display: flex;
        flex-direction: column;
        gap: 6px;
      }
      .pred-kpi span {
        font-size: 12px;
        color: var(--text-muted);
        text-transform: uppercase;
        letter-spacing: 0.04em;
      }
      .pred-kpi strong {
        font-size: 22px;
        font-family: var(--font-heading);
      }
      .pred-section-head {
        display: flex;
        align-items: baseline;
        justify-content: space-between;
        gap: 12px;
        padding: 8px 8px 12px;
      }
      .pred-section-head h2 {
        margin: 0;
        font-size: 15px;
      }
      .pred-section-head span {
        font-size: 12.5px;
        color: var(--text-muted);
      }
      .pred-table td small {
        display: block;
        color: var(--text-muted);
        font-size: 11.5px;
      }
      .pred-days {
        display: flex;
        flex-wrap: wrap;
        gap: 4px;
      }
      .pred-day {
        padding: 2px 6px;
        border-radius: 999px;
        font-size: 11.5px;
        border: 1px solid transparent;
      }
      .pred-day--high {
        background: var(--danger-50, #fef2f2);
        color: var(--danger-700, #b91c1c);
        border-color: var(--danger-200, #fecaca);
      }
      .pred-day--mid {
        background: var(--warning-50, #fffbeb);
        color: var(--warning-700, #b45309);
        border-color: var(--warning-200, #fde68a);
      }
      .pred-day--low {
        background: var(--success-50, #f0fdf4);
        color: var(--success-700, #15803d);
        border-color: var(--success-200, #bbf7d0);
      }
      .pred-empty {
        padding: 18px;
        text-align: center;
        color: var(--text-muted);
        font-size: 13px;
      }
      .pred-table {
        width: 100%;
      }
    `,
  ],
})
export class PredictionsPage implements OnInit {
  private readonly api = inject(PredictionsService);

  readonly status = signal<MlStatus | null>(null);
  readonly absences = signal<MlAbsenceForecast[]>([]);
  readonly hs = signal<MlHsForecast[]>([]);
  readonly job = signal<MlJobState | null>(null);
  readonly loading = signal(false);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);

  readonly horizonJours = 14;

  readonly modeles = computed<MlModelInfo[]>(() => this.status()?.modeles ?? []);
  readonly salariesARisque = computed(() => this.absences().filter((a) => a.niveau >= 3).length);
  readonly risqueMoyen = computed(() => {
    const list = this.absences();
    return list.length ? list.reduce((sum, a) => sum + a.risque, 0) / list.length : 0;
  });
  readonly hsTotal = computed(() => this.hs().reduce((sum, h) => sum + h.heuresPrevues, 0));
  readonly absenceQualite = computed(() => {
    const model = this.modeles().find((m) => m.estActif && m.typeModele === 'Absence');
    const auc = this.metric(model, 'auc');
    return auc == null ? '—' : auc.toFixed(3);
  });
  readonly semaineLabel = computed(() => {
    const semaine = this.hs()[0]?.semaineDebut;
    return semaine ? `semaine du ${this.jourCourt(semaine)}` : 'semaine en cours';
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      status: this.api.status(),
      absences: this.api.absence({}),
      hs: this.api.overtime(),
      job: this.api.job(),
    }).subscribe({
      next: (result) => {
        this.status.set(result.status);
        this.absences.set(result.absences);
        this.hs.set(result.hs);
        this.job.set(result.job);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err?.error?.message ?? 'Impossible de charger les prévisions.');
        this.loading.set(false);
      },
    });
  }

  action(kind: 'refresh' | 'train' | 'backfill'): void {
    this.info.set(null);
    this.error.set(null);
    const request = kind === 'refresh' ? this.api.refresh() : kind === 'train' ? this.api.train() : this.api.backfill();
    request.subscribe({
      next: (result) => {
        this.info.set(result.message);
        this.load();
      },
      error: (err) => this.error.set(err?.error?.message ?? 'Action refusée.'),
    });
  }

  fmtDate(value: string | null | undefined): string {
    return formatIsoDate(value);
  }

  jourCourt(value: string | null | undefined): string {
    if (!value) return '';
    const s = String(value).slice(0, 10);
    const [, mois, jour] = s.split('-');
    return jour && mois ? `${jour}/${mois}` : s;
  }

  pct(value: number | null | undefined): string {
    return `${Math.round((value ?? 0) * 100)} %`;
  }

  etatLabel(): string {
    switch (this.status()?.etat) {
      case 'Pret':
        return 'Moteur prêt';
      case 'Baseline':
        return 'Mode baseline';
      case 'Desactive':
        return 'Moteur désactivé';
      default:
        return 'État inconnu';
    }
  }

  etatClass(): string {
    return (this.status()?.etat ?? 'Desactive').toLowerCase();
  }

  niveauVariant(niveau: number): 'danger' | 'warning' | 'success' {
    if (niveau >= 3) return 'danger';
    if (niveau >= 2) return 'warning';
    return 'success';
  }

  niveauClass(niveau: number): string {
    if (niveau >= 3) return 'high';
    if (niveau >= 2) return 'mid';
    return 'low';
  }

  typeLabel(type: string): string {
    switch (type) {
      case 'Absence':
        return 'Absence';
      case 'HsProbabilite':
        return 'HS — probabilité';
      case 'HsMontant':
        return 'HS — montant';
      default:
        return type;
    }
  }

  statutVariant(model: MlModelInfo): 'success' | 'warning' | 'danger' | 'muted' {
    if (model.estActif) return 'success';
    if (model.statut === 'Rejete' || model.statut === 'Echec') return 'danger';
    return 'muted';
  }

  metricLabel(model: MlModelInfo): string {
    if (model.typeModele === 'HsMontant') {
      const mae = this.metric(model, 'mae');
      const base = this.metric(model, 'baselineMae');
      if (mae == null) return '—';
      return `MAE ${mae.toFixed(2)} h${base != null ? ` (base ${base.toFixed(2)} h)` : ''}`;
    }
    const auc = this.metric(model, 'auc');
    const base = this.metric(model, 'baselineAuc');
    if (auc == null) return '—';
    return `AUC ${auc.toFixed(3)}${base != null ? ` (base ${base.toFixed(3)})` : ''}`;
  }

  private metric(model: MlModelInfo | undefined | null, key: string): number | null {
    if (!model?.metriquesJson) return null;
    try {
      const parsed = JSON.parse(model.metriquesJson) as Record<string, unknown>;
      const pascal = key.charAt(0).toUpperCase() + key.slice(1);
      const value = parsed[pascal] ?? parsed[key];
      return typeof value === 'number' ? value : null;
    } catch {
      return null;
    }
  }
}
