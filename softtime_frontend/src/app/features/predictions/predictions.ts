import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { finalize, forkJoin } from 'rxjs';
import {
  MlHsAccuracy,
  MlHsForecast,
  MlHsWeek,
  MlJobState,
  MlModelInfo,
  MlStatus,
} from '../../shared/models';
import { PredictionsService } from '../../shared/services/predictions.service';
import {
  Column,
  DataTable,
  DataTableCell,
  PageHeader,
  SoftBadge,
  SoftButton,
  SoftCard,
} from '../../shared/components';
import { fmtDate as formatIsoDate } from '../../shared/utils/date';

/** Heures supplémentaires prévues d'un département (agrégat des lignes salarié). */
interface HsDepartement {
  nom: string;
  salaries: number;
  auDelaPlafond: number;
  heures: number;
  borneBasse: number;
  borneHaute: number;
  part: number;
}

const SANS_DEPARTEMENT = 'Sans département';

/** Heures à une décimale, séparateurs français (16 248,1). */
const HEURES = new Intl.NumberFormat('fr-FR', { minimumFractionDigits: 1, maximumFractionDigits: 1 });

/**
 * Moteur prédictif : état des modèles et heures supplémentaires prévues pour la semaine, par
 * département puis par salarié. Les prédictions sont précalculées côté serveur ; cette page ne
 * fait que les agréger, les filtrer et les présenter.
 */
@Component({
  selector: 'app-predictions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, SoftCard, SoftButton, SoftBadge, DataTable, DataTableCell, NgTemplateOutlet],
  template: `
    <page-header
      title="Prévisions"
      subtitle="Heures supplémentaires attendues cette semaine, estimées par le moteur ML.NET"
    >
      <soft-button variant="secondary" (click)="load()" [loading]="loading()">Actualiser</soft-button>
      <soft-button variant="secondary" (click)="action('refresh')" [loading]="acting() === 'refresh'" [disabled]="(job()?.running ?? false) || acting() !== null">
        Rafraîchir les données
      </soft-button>
      <soft-button variant="secondary" (click)="action('train')" [loading]="acting() === 'train'" [disabled]="(job()?.running ?? false) || acting() !== null">
        Réentraîner
      </soft-button>
      <soft-button variant="ghost" (click)="action('backfill')" [loading]="acting() === 'backfill'" [disabled]="(job()?.running ?? false) || acting() !== null">
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
        Aucun modèle d'heures supplémentaires actif pour cette société : les données sont insuffisantes ou
        le modèle ne bat pas encore la baseline. Lancez « Reconstruire l'historique » puis « Réentraîner ».
      </div>
    } @else {
      <soft-card variant="bordered" padding="sm">
        <div class="pred-section-head">
          <h2>Horizon</h2>
          <span>Choisissez la semaine à analyser</span>
        </div>
        <div class="pred-weeks">
          @for (w of horizon(); track w.semaineDebut; let i = $index) {
            <button
              type="button"
              class="pred-week"
              [class.pred-week--active]="estSemaineChoisie(w.semaineDebut)"
              [class.pred-week--empty]="w.salaries === 0"
              [attr.aria-pressed]="estSemaineChoisie(w.semaineDebut)"
              (click)="choisirSemaine(w.semaineDebut)"
            >
              <span class="pred-week__label">{{ i === 0 ? 'Cette semaine' : 'S+' + i }}</span>
              <small>du {{ jourCourt(w.semaineDebut) }}</small>
              <span class="pred-week__bar" aria-hidden="true">
                <span class="pred-week__fill" [style.height.%]="barreSemaine(w.heuresPrevues)"></span>
              </span>
              @if (w.salaries > 0) {
                <strong>{{ heures(w.heuresPrevues) }}</strong>
                <small>{{ w.salaries }} salarié(s) · {{ w.auDelaPlafond }} > {{ plafond() }} h</small>
              } @else {
                <strong>—</strong>
                <small>planning non saisi</small>
              }
            </button>
          }
        </div>
      </soft-card>

      <div class="pred-kpis" [class.pred-loading]="hsLoading()" [attr.aria-busy]="hsLoading()">
        <soft-card variant="bordered">
          <div class="pred-kpi">
            <span>HS prévues ({{ semaineLabel() }})</span>
            <strong>{{ heures(hsTotal()) }}</strong>
            <small>fourchette {{ heures(hsBasse()) }} – {{ heures(hsHaute()) }}</small>
          </div>
        </soft-card>
        <soft-card variant="bordered">
          <div class="pred-kpi">
            <span>Au-delà de {{ plafond() }} h</span>
            <strong>{{ auDelaPlafond() }} / {{ hs().length }}</strong>
            <small>salariés dont les HS deviennent imposables</small>
          </div>
        </soft-card>
        <soft-card variant="bordered">
          <div class="pred-kpi">
            <span>Département le plus chargé</span>
            @if (departementMax(); as d) {
              <strong class="pred-kpi__text" [title]="d.nom">{{ d.nom }}</strong>
              <small>{{ heures(d.heures) }} · {{ pct(d.part) }} du total</small>
            } @else {
              <strong>—</strong>
              <small>aucune HS prévue</small>
            }
          </div>
        </soft-card>
        <soft-card variant="bordered">
          <div class="pred-kpi">
            <span>Précision du modèle</span>
            <strong>{{ precision().valeur }}</strong>
            <small>{{ precision().source }}</small>
          </div>
        </soft-card>
      </div>

      <soft-card variant="bordered" padding="sm">
        <div class="pred-section-head">
          <h2>Par département</h2>
          <span>Cliquez sur un département pour voir ses salariés</span>
        </div>
        @if (hsLoading()) {
          <div class="pred-empty">Chargement de la semaine…</div>
        } @else if (departements().length === 0) {
          <div class="pred-empty">
            Aucune prévision pour cette semaine : le planning n'est pas encore saisi ou le modèle est indisponible.
          </div>
        } @else {
          <div class="pred-depts">
            @for (d of departements(); track d.nom) {
              <button
                type="button"
                class="pred-dept"
                [class.pred-dept--active]="departementFiltre() === d.nom"
                [attr.aria-pressed]="departementFiltre() === d.nom"
                (click)="filtrerDepartement(d.nom)"
              >
                <span class="pred-dept__name">
                  <strong>{{ d.nom }}</strong>
                  <small>
                    {{ d.salaries }} salarié{{ d.salaries > 1 ? 's' : '' }}
                    @if (d.auDelaPlafond > 0) {
                      · {{ d.auDelaPlafond }} > {{ plafond() }} h
                    }
                  </small>
                </span>
                <span class="pred-dept__bar" aria-hidden="true">
                  <span class="pred-dept__fill" [style.width.%]="barre(d)"></span>
                </span>
                <span class="pred-dept__value">
                  <strong>{{ heures(d.heures) }}</strong>
                  <small>{{ heures(d.borneBasse) }} – {{ heures(d.borneHaute) }}</small>
                </span>
                <span class="pred-dept__share">{{ pct(d.part) }}</span>
              </button>
            }
          </div>
        }
      </soft-card>

      <soft-card variant="bordered" padding="sm">
        <div class="pred-section-head">
          <h2>Par salarié</h2>
          @if (departementFiltre(); as filtre) {
            <button type="button" class="pred-filter" (click)="filtrerDepartement(null)" title="Retirer le filtre">
              {{ filtre }} <span aria-hidden="true">✕</span>
            </button>
          } @else {
            <span>{{ hs().length }} salarié(s) · {{ semaineLabel() }}</span>
          }
        </div>
        <data-table
          [columns]="hsColumns"
          [rows]="hsRows()"
          trackKey="matricule"
          searchPlaceholder="Rechercher un salarié, un matricule, un département…"
          emptyText="Aucune prévision HS : planning de la semaine incomplet ou modèle indisponible."
        >
          <ng-template dataTableCell="salarie" let-row>
            <ng-container *ngTemplateOutlet="salarieCell; context: { $implicit: row }"></ng-container>
          </ng-template>
          <ng-template dataTableCell="heuresPrevues" let-row>
            <strong>{{ heures(hsLigne(row).heuresPrevues) }}</strong>
          </ng-template>
          <ng-template dataTableCell="probabiliteDepassement" let-row>
            <soft-badge [variant]="niveauVariant(hsLigne(row).niveau)">
              {{ niveauLabel(hsLigne(row).niveau) }} · {{ pct(hsLigne(row).probabiliteDepassement) }}
            </soft-badge>
          </ng-template>
        </data-table>
      </soft-card>

      <soft-card variant="bordered" padding="sm">
        <div class="pred-section-head">
          <h2>Prévu / réel</h2>
          <span>Semaines terminées : dernière prévision émise comparée aux HS constatées</span>
        </div>
        @if (suivi().length === 0) {
          <div class="pred-empty">
            Pas encore d'historique : la comparaison apparaît dès qu'une semaine prévue est terminée et recalculée.
          </div>
        } @else {
          <div class="pred-accuracy">
            <div>
              <span>Écart moyen par salarié</span>
              <strong>± {{ heures(suiviSynthese().ecartMoyen) }}</strong>
            </div>
            <div>
              <span>Réel dans la fourchette</span>
              <strong>{{ pct(suiviSynthese().couverture) }}</strong>
            </div>
            <div>
              <span>Total prévu → réel</span>
              <strong>{{ heures(suiviSynthese().prevu) }} → {{ heures(suiviSynthese().reel) }}</strong>
            </div>
            <div>
              <span>Biais</span>
              <strong [class]="ecartClass(suiviSynthese().reel - suiviSynthese().prevu)">
                {{ biaisLabel(suiviSynthese().prevu, suiviSynthese().reel) }}
              </strong>
            </div>
          </div>
          <data-table
            [columns]="suiviColumns"
            [rows]="suiviRows()"
            trackKey="semaineDebut"
            [searchable]="false"
            [paginate]="false"
            emptyText="Aucune semaine comparable."
          >
            <ng-template dataTableCell="ecart" let-row>
              <span [class]="ecartClass(suiviLigne(row).heuresReelles - suiviLigne(row).heuresPrevues)">
                {{ ecartSigne(suiviLigne(row).heuresReelles - suiviLigne(row).heuresPrevues) }}
              </span>
            </ng-template>
          </data-table>
        }
      </soft-card>
    }

    <ng-template #salarieCell let-row>
      <strong>{{ row['nom'] }} {{ row['prenom'] }}</strong>
      <small class="pred-sub">{{ row['matricule'] }}</small>
    </ng-template>

    <soft-card variant="bordered" padding="sm">
      <div class="pred-section-head">
        <h2>Modèles</h2>
        <span>Registre versionné des entraînements</span>
      </div>
      <data-table
        [columns]="modeleColumns"
        [rows]="modeleRows()"
        trackKey="id"
        searchPlaceholder="Rechercher un type, une version, un statut…"
        emptyText="Aucun entraînement enregistré."
      >
        <ng-template dataTableCell="statut" let-row>
          <soft-badge [variant]="statutVariant(modele(row))">{{ modele(row).statut }}</soft-badge>
        </ng-template>
      </data-table>
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
        min-width: 0;
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
      .pred-kpi__text {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .pred-kpi small {
        font-size: 12px;
        color: var(--text-muted);
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
      .pred-sub {
        display: block;
        color: var(--text-muted);
        font-size: 11.5px;
      }
      .pred-depts {
        display: flex;
        flex-direction: column;
        gap: 4px;
        padding: 0 8px 8px;
      }
      .pred-dept {
        display: grid;
        grid-template-columns: minmax(140px, 1.2fr) minmax(80px, 2fr) minmax(90px, auto) 48px;
        align-items: center;
        gap: 14px;
        width: 100%;
        padding: 8px 10px;
        border: 1px solid transparent;
        border-radius: var(--radius-md);
        background: none;
        color: inherit;
        font: inherit;
        text-align: left;
        cursor: pointer;
      }
      .pred-dept:hover {
        background: var(--primary-50);
      }
      .pred-dept--active {
        border-color: var(--primary-300);
        background: var(--primary-50);
      }
      .pred-dept__name,
      .pred-dept__value {
        display: flex;
        flex-direction: column;
        gap: 2px;
        min-width: 0;
      }
      .pred-dept__name strong {
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
        font-size: 13.5px;
      }
      .pred-dept__name small,
      .pred-dept__value small {
        color: var(--text-muted);
        font-size: 11.5px;
      }
      .pred-dept__value {
        text-align: right;
      }
      .pred-dept__bar {
        height: 8px;
        border-radius: 999px;
        background: var(--app-border);
        overflow: hidden;
      }
      .pred-dept__fill {
        display: block;
        height: 100%;
        border-radius: 999px;
        background: var(--primary-500);
      }
      .pred-dept__share {
        font-size: 12.5px;
        color: var(--text-muted);
        text-align: right;
      }
      .pred-filter {
        display: inline-flex;
        align-items: center;
        gap: 6px;
        padding: 2px 10px;
        border: 1px solid var(--primary-300);
        border-radius: 999px;
        background: var(--primary-50);
        color: inherit;
        font: inherit;
        font-size: 12.5px;
        cursor: pointer;
      }
      .pred-weeks {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(130px, 1fr));
        gap: 8px;
        padding: 0 8px 8px;
      }
      .pred-week {
        display: flex;
        flex-direction: column;
        align-items: flex-start;
        gap: 4px;
        padding: 10px 12px;
        border: 1px solid var(--app-border);
        border-radius: var(--radius-md);
        background: none;
        color: inherit;
        font: inherit;
        text-align: left;
        cursor: pointer;
      }
      .pred-week:hover {
        background: var(--primary-50);
      }
      .pred-week--active {
        border-color: var(--primary-300);
        background: var(--primary-50);
      }
      .pred-week--empty strong {
        color: var(--text-muted);
      }
      .pred-week__label {
        font-size: 12px;
        font-weight: 600;
        text-transform: uppercase;
        letter-spacing: 0.04em;
      }
      .pred-week small {
        font-size: 11.5px;
        color: var(--text-muted);
      }
      .pred-week strong {
        font-size: 18px;
        font-family: var(--font-heading);
      }
      .pred-week__bar {
        display: flex;
        align-items: flex-end;
        width: 100%;
        height: 36px;
        border-bottom: 1px solid var(--app-border);
      }
      .pred-week__fill {
        display: block;
        width: 100%;
        border-radius: 4px 4px 0 0;
        background: var(--primary-500);
      }
      .pred-accuracy {
        display: grid;
        grid-template-columns: repeat(auto-fit, minmax(160px, 1fr));
        gap: 12px;
        padding: 0 8px 12px;
      }
      .pred-accuracy div {
        display: flex;
        flex-direction: column;
        gap: 4px;
      }
      .pred-accuracy span {
        font-size: 12px;
        color: var(--text-muted);
      }
      .pred-accuracy strong {
        font-size: 17px;
        font-family: var(--font-heading);
      }
      .pred-ecart--plus {
        color: var(--danger-700, #b91c1c);
      }
      .pred-ecart--moins {
        color: var(--success-700, #15803d);
      }
      .pred-loading {
        opacity: 0.5;
        transition: opacity 0.15s;
      }
      .pred-empty {
        padding: 18px;
        text-align: center;
        color: var(--text-muted);
        font-size: 13px;
      }
      @media (max-width: 640px) {
        .pred-dept {
          grid-template-columns: 1fr auto;
          gap: 6px 12px;
        }
        .pred-dept__bar {
          grid-column: 1 / -1;
          grid-row: 2;
        }
        .pred-dept__share {
          display: none;
        }
      }
    `,
  ],
})
export class PredictionsPage implements OnInit {
  private readonly api = inject(PredictionsService);

  readonly status = signal<MlStatus | null>(null);
  readonly hs = signal<MlHsForecast[]>([]);
  readonly horizon = signal<MlHsWeek[]>([]);
  readonly suivi = signal<MlHsAccuracy[]>([]);
  readonly job = signal<MlJobState | null>(null);
  readonly loading = signal(false);
  /** Action de maintenance en cours d'envoi (boutons en chargement, les autres bloqués). */
  readonly acting = signal<'refresh' | 'train' | 'backfill' | null>(null);
  readonly hsLoading = signal(false);
  /** Lundi (AAAA-MM-JJ) de la semaine analysée ; null : semaine en cours. */
  readonly semaine = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  readonly info = signal<string | null>(null);
  /** Département sélectionné dans la vue agrégée ; filtre la liste des salariés. */
  readonly departementFiltre = signal<string | null>(null);

  readonly modeles = computed<MlModelInfo[]>(() => this.status()?.modeles ?? []);

  // Tableaux partagés (data-table) : le texte des colonnes (format) sert aussi à la recherche.
  readonly hsRows = computed(() => {
    const filtre = this.departementFiltre();
    const lignes = filtre ? this.hs().filter((h) => this.nomDepartement(h) === filtre) : this.hs();
    return lignes as unknown as Record<string, unknown>[];
  });
  readonly modeleRows = computed(() => this.modeles() as unknown as Record<string, unknown>[]);
  readonly suiviRows = computed(() => this.suivi() as unknown as Record<string, unknown>[]);

  readonly suiviColumns: Column[] = [
    { key: 'semaineDebut', label: 'Semaine', format: (v) => `du ${this.jourCourt(v as string)}` },
    { key: 'salaries', label: 'Salariés' },
    { key: 'heuresPrevues', label: 'Prévu', format: (v) => this.heures(v as number) },
    { key: 'heuresReelles', label: 'Réel', format: (v) => this.heures(v as number) },
    {
      key: 'ecart',
      label: 'Écart',
      format: (_, row) => this.ecartSigne(this.suiviLigne(row).heuresReelles - this.suiviLigne(row).heuresPrevues),
    },
    { key: 'ecartMoyen', label: 'Écart moyen / salarié', format: (v) => `± ${this.heures(v as number)}` },
    { key: 'couverture', label: 'Dans la fourchette', format: (v) => this.pct(v as number) },
  ];

  readonly hsColumns: Column[] = [
    { key: 'salarie', label: 'Salarié', format: (_, row) => this.salarieTexte(row) },
    { key: 'departement', label: 'Département', format: (v) => (v as string | null) || '—' },
    { key: 'heuresPrevues', label: 'HS prévues', format: (v) => this.heures(v as number) },
    {
      key: 'intervalle',
      label: 'Fourchette',
      format: (_, row) => `${this.heures(this.hsLigne(row).borneBasse)} – ${this.heures(this.hsLigne(row).borneHaute)}`,
    },
    {
      key: 'probabiliteDepassement',
      label: "Probabilité d'HS",
      format: (v, row) => `${this.niveauLabel(this.hsLigne(row).niveau)} ${this.pct(v as number)}`,
    },
  ];

  readonly modeleColumns: Column[] = [
    { key: 'typeModele', label: 'Type', format: (v) => this.typeLabel(v as string) },
    { key: 'version', label: 'Version' },
    { key: 'algorithme', label: 'Algorithme' },
    { key: 'statut', label: 'Statut' },
    { key: 'entraineLe', label: 'Entraîné le', format: (v) => this.fmtDate(v as string) },
    { key: 'metriques', label: 'Métriques', format: (_, row) => this.metricLabel(this.modele(row)) },
    { key: 'commentaire', label: 'Commentaire', format: (v) => (v as string | null) || '—' },
  ];

  readonly hsTotal = computed(() => this.hs().reduce((sum, h) => sum + h.heuresPrevues, 0));
  readonly hsBasse = computed(() => this.hs().reduce((sum, h) => sum + h.borneBasse, 0));
  readonly hsHaute = computed(() => this.hs().reduce((sum, h) => sum + h.borneHaute, 0));
  /** Plafond hebdomadaire des HS exonérées (OvertimeRules.ExemptCeilingHours côté serveur). */
  readonly plafond = computed(() => this.status()?.plafondHsExonere ?? 20);
  readonly auDelaPlafond = computed(() => this.hs().filter((h) => h.heuresPrevues > this.plafond()).length);

  readonly departements = computed<HsDepartement[]>(() => {
    const total = this.hsTotal();
    const groupes = new Map<string, HsDepartement>();
    for (const h of this.hs()) {
      const nom = this.nomDepartement(h);
      const d = groupes.get(nom) ?? { nom, salaries: 0, auDelaPlafond: 0, heures: 0, borneBasse: 0, borneHaute: 0, part: 0 };
      d.salaries++;
      if (h.heuresPrevues > this.plafond()) d.auDelaPlafond++;
      d.heures += h.heuresPrevues;
      d.borneBasse += h.borneBasse;
      d.borneHaute += h.borneHaute;
      groupes.set(nom, d);
    }
    return [...groupes.values()]
      .map((d) => ({ ...d, part: total > 0 ? d.heures / total : 0 }))
      .sort((a, b) => b.heures - a.heures || a.nom.localeCompare(b.nom));
  });
  readonly departementMax = computed(() => {
    const premier = this.departements()[0];
    return premier && premier.heures > 0 ? premier : null;
  });

  /** Synthèse prévu / réel, pondérée par le nombre de salariés comparés chaque semaine. */
  readonly suiviSynthese = computed(() => {
    const lignes = this.suivi();
    const salaries = lignes.reduce((sum, s) => sum + s.salaries, 0);
    const pondere = (f: (s: MlHsAccuracy) => number) =>
      salaries > 0 ? lignes.reduce((sum, s) => sum + f(s) * s.salaries, 0) / salaries : 0;
    return {
      semaines: lignes.length,
      ecartMoyen: pondere((s) => s.ecartMoyen),
      couverture: pondere((s) => s.couverture),
      prevu: lignes.reduce((sum, s) => sum + s.heuresPrevues, 0),
      reel: lignes.reduce((sum, s) => sum + s.heuresReelles, 0),
    };
  });

  /**
   * Précision affichée : l'écart constaté en conditions réelles dès qu'une semaine est comparable,
   * sinon l'écart mesuré sur les semaines de test à l'entraînement.
   */
  readonly precision = computed(() => {
    const synthese = this.suiviSynthese();
    if (synthese.semaines > 0) {
      return {
        valeur: `± ${this.heures(synthese.ecartMoyen)}`,
        source: `constaté sur ${synthese.semaines} semaine(s) terminée(s)`,
      };
    }
    const model = this.modeles().find((m) => m.estActif && m.typeModele === 'HsMontant');
    const mae = this.metric(model, 'mae');
    return {
      valeur: mae == null ? '—' : `± ${this.heures(mae)}`,
      source: "mesuré à l'entraînement, par salarié et par semaine",
    };
  });
  /** Lundi de la semaine affichée : choix de l'utilisateur, sinon première semaine de l'horizon. */
  readonly semaineAffichee = computed(
    () => this.semaine() ?? this.jourIso(this.horizon()[0]?.semaineDebut ?? this.hs()[0]?.semaineDebut),
  );
  readonly semaineLabel = computed(() => {
    const semaine = this.semaineAffichee();
    return semaine ? `semaine du ${this.jourCourt(semaine)}` : 'semaine en cours';
  });
  private readonly horizonMax = computed(() => Math.max(0, ...this.horizon().map((w) => w.heuresPrevues)));

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      status: this.api.status(),
      horizon: this.api.horizon(),
      suivi: this.api.accuracy(),
      hs: this.api.overtime(this.semaine() ?? undefined),
      job: this.api.job(),
    }).subscribe({
      next: (result) => {
        this.status.set(result.status);
        this.horizon.set(result.horizon);
        this.suivi.set(result.suivi);
        this.job.set(result.job);
        this.appliquerSemaine(result.hs);
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
    this.acting.set(kind);
    request.pipe(finalize(() => this.acting.set(null))).subscribe({
      next: (result) => {
        this.info.set(result.message);
        this.load();
      },
      error: (err) => this.error.set(err?.error?.message ?? 'Action refusée.'),
    });
  }

  /** Charge les prévisions par salarié d'une semaine de l'horizon. */
  choisirSemaine(semaineDebut: string): void {
    const lundi = this.jourIso(semaineDebut);
    if (!lundi || lundi === this.semaineAffichee()) return;
    this.semaine.set(lundi);
    this.hsLoading.set(true);
    this.api.overtime(lundi).subscribe({
      next: (hs) => {
        this.appliquerSemaine(hs);
        this.hsLoading.set(false);
      },
      error: (err) => {
        this.error.set(err?.error?.message ?? 'Impossible de charger la semaine.');
        this.hsLoading.set(false);
      },
    });
  }

  estSemaineChoisie(semaineDebut: string): boolean {
    return this.jourIso(semaineDebut) === this.semaineAffichee();
  }

  /** Hauteur de la barre d'une semaine, relative à la semaine la plus chargée de l'horizon. */
  barreSemaine(heures: number): number {
    const max = this.horizonMax();
    return max > 0 && heures > 0 ? Math.max(4, (heures / max) * 100) : 0;
  }

  private appliquerSemaine(hs: MlHsForecast[]): void {
    this.hs.set(hs);
    // Un filtre sur un département absent de la semaine viderait la liste sans raison visible.
    if (!hs.some((h) => this.nomDepartement(h) === this.departementFiltre())) {
      this.departementFiltre.set(null);
    }
  }

  /** Sélectionne un département (un second clic le désélectionne). */
  filtrerDepartement(nom: string | null): void {
    this.departementFiltre.set(nom === this.departementFiltre() ? null : nom);
  }

  /** Largeur de la barre, relative au département le plus chargé. */
  barre(d: HsDepartement): number {
    const max = this.departements()[0]?.heures ?? 0;
    return max > 0 ? Math.max(2, (d.heures / max) * 100) : 0;
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

  heures(value: number | null | undefined): string {
    return `${HEURES.format(value ?? 0)} h`;
  }

  /** Écart réel − prévu signé : positif, plus d'HS que prévu. */
  ecartSigne(ecart: number): string {
    return `${ecart > 0 ? '+' : ecart < 0 ? '−' : ''}${this.heures(Math.abs(ecart))}`;
  }

  ecartClass(ecart: number): string {
    if (ecart > 0.05) return 'pred-ecart--plus';
    if (ecart < -0.05) return 'pred-ecart--moins';
    return '';
  }

  /** Sens global de l'erreur : le modèle sous-estime ou surestime le total. */
  biaisLabel(prevu: number, reel: number): string {
    if (prevu <= 0) return reel > 0 ? 'Sous-estimé' : '—';
    const ratio = (reel - prevu) / prevu;
    if (Math.abs(ratio) < 0.05) return 'Équilibré';
    return `${ratio > 0 ? 'Sous-estimé' : 'Surestimé'} de ${this.pct(Math.abs(ratio))}`;
  }

  private jourIso(value: string | null | undefined): string | null {
    return value ? String(value).slice(0, 10) : null;
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

  /** Texte du niveau, pour que la recherche trouve « élevée », « moyenne » ou « faible ». */
  niveauLabel(niveau: number): string {
    if (niveau >= 3) return 'Élevée';
    if (niveau >= 2) return 'Moyenne';
    return 'Faible';
  }

  // Lignes du data-table, typées pour les modèles de cellule.
  hsLigne(row: Record<string, unknown>): MlHsForecast {
    return row as unknown as MlHsForecast;
  }

  suiviLigne(row: Record<string, unknown>): MlHsAccuracy {
    return row as unknown as MlHsAccuracy;
  }

  modele(row: Record<string, unknown>): MlModelInfo {
    return row as unknown as MlModelInfo;
  }

  private nomDepartement(h: MlHsForecast): string {
    return h.departement?.trim() || SANS_DEPARTEMENT;
  }

  private salarieTexte(row: Record<string, unknown>): string {
    return [row['nom'], row['prenom'], row['matricule']].filter(Boolean).join(' ');
  }

  typeLabel(type: string): string {
    switch (type) {
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
