import { ChangeDetectionStrategy, Component, computed, inject, OnInit, signal } from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { WeekLanding, WeekLandingRow, WeekLandingWeek } from '../../shared/models';
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

/** Atterrissage des heures supplémentaires d'un département (agrégat des lignes salarié). */
interface HsDepartement {
  nom: string;
  salaries: number;
  /** Salariés qui passent à 150 % ou au-delà. */
  alertes: number;
  acquises: number;
  heures: number;
  part: number;
}

const SANS_DEPARTEMENT = 'Sans département';

/** Seuil à partir duquel un salarié est une alerte : passage à 150 %. */
const NIVEAU_ALERTE = 2;

/** Heures à une décimale, séparateurs français (16 248,1). */
const HEURES = new Intl.NumberFormat('fr-FR', { minimumFractionDigits: 1, maximumFractionDigits: 1 });

/**
 * Atterrissage de la semaine : heures supplémentaires que chaque salarié aura faites dimanche soir,
 * d'après les pointages déjà traités et le planning des jours restants. Le calcul est fait côté
 * serveur à chaque lecture ; cette page l'agrège, le filtre et le présente.
 */
@Component({
  selector: 'app-predictions',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, SoftCard, SoftButton, SoftBadge, DataTable, DataTableCell, NgTemplateOutlet, RouterLink],
  template: `
    <page-header
      title="Prévisions"
      subtitle="Atterrissage des heures supplémentaires : ce que chaque salarié aura fait dimanche soir"
    >
      <a class="soft-btn soft-btn--ghost" routerLink="/employee-planning">Ouvrir le planning</a>
      <soft-button variant="secondary" (click)="load()" [loading]="loading()">Actualiser</soft-button>
    </page-header>

    @if (error()) {
      <div class="pred-alert pred-alert--error" role="alert">{{ error() }}</div>
    }

    @if (loading() && !landing()) {
      <div class="pred-empty">Calcul de l'atterrissage…</div>
    } @else if (landing(); as l) {
      <div class="pred-status">
        <span class="pred-status__dot" [class]="'pred-status__dot pred-status__dot--' + fraicheur().etat"></span>
        <div class="pred-status__text">
          <strong>{{ fraicheur().titre }}</strong>
          <span>{{ fraicheur().detail }}</span>
        </div>
        <span class="pred-status__meta">
          Seuils : 150 % au-delà de {{ heures(l.seuilMajoration150) }}, imposable au-delà de {{ heures(l.plafondExonere) }}
        </span>
      </div>

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
                <span class="pred-week__fill" [style.height.%]="barreSemaine(w.hsAtterrissage)"></span>
              </span>
              @if (w.salaries > 0) {
                <strong>{{ heures(w.hsAtterrissage) }}</strong>
                <small>{{ w.enHeuresSup }} en HS · {{ w.auDela150 }} à 150 %</small>
              } @else {
                <strong>—</strong>
                <small>planning non saisi</small>
              }
            </button>
          }
        </div>
      </soft-card>

      <div class="pred-kpis" [class.pred-loading]="weekLoading()" [attr.aria-busy]="weekLoading()">
        <soft-card variant="bordered">
          <div class="pred-kpi">
            <span>HS à l'atterrissage ({{ semaineLabel() }})</span>
            <strong>{{ heures(l.totaux.hsAtterrissage) }}</strong>
            <small>dont {{ heures(l.totaux.hsAcquises) }} déjà acquises</small>
          </div>
        </soft-card>
        <soft-card variant="bordered">
          <div class="pred-kpi">
            <span>Salariés en heures supplémentaires</span>
            <strong>{{ l.totaux.enHeuresSup }} / {{ l.totaux.salaries }}</strong>
            <small>finiront la semaine au-dessus de leur contrat</small>
          </div>
        </soft-card>
        <soft-card variant="bordered">
          <div class="pred-kpi">
            <span>Passent à 150 %</span>
            <strong>{{ l.totaux.auDela150 }}</strong>
            <small>dont {{ l.totaux.auDelaPlafond }} au-delà de {{ heures(l.plafondExonere) }} (imposable)</small>
          </div>
        </soft-card>
        <soft-card variant="bordered">
          <div class="pred-kpi">
            <span>Heures encore évitables</span>
            <strong>{{ heures(l.totaux.heuresARetirer) }}</strong>
            <small>à retirer des plannings restants pour repasser sous les seuils</small>
          </div>
        </soft-card>
      </div>

      @if (l.totaux.sansPlanning > 0) {
        <div class="pred-alert pred-alert--warn" role="status">
          {{ l.totaux.sansPlanning }} salarié(s) ont au moins un jour restant sans planning : leur atterrissage est
          sous-estimé tant que le planning n'est pas saisi.
        </div>
      }

      <soft-card variant="bordered" padding="sm">
        <div class="pred-section-head">
          <h2>Par département</h2>
          <span>Cliquez sur un département pour voir ses salariés</span>
        </div>
        @if (weekLoading()) {
          <div class="pred-empty">Calcul de la semaine…</div>
        } @else if (departements().length === 0) {
          <div class="pred-empty">Aucun salarié n'a de pointage ni de planning sur cette semaine.</div>
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
                    @if (d.alertes > 0) {
                      · {{ d.alertes }} à 150 %
                    }
                  </small>
                </span>
                <span class="pred-dept__bar" aria-hidden="true">
                  <span class="pred-dept__fill" [style.width.%]="barre(d)"></span>
                </span>
                <span class="pred-dept__value">
                  <strong>{{ heures(d.heures) }}</strong>
                  <small>dont {{ heures(d.acquises) }} acquises</small>
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
          <span class="pred-filters">
            @if (departementFiltre(); as filtre) {
              <button type="button" class="pred-filter" (click)="filtrerDepartement(null)" title="Retirer le filtre">
                {{ filtre }} <span aria-hidden="true">✕</span>
              </button>
            }
            <button
              type="button"
              class="pred-filter"
              [class.pred-filter--off]="!alertesSeules()"
              [attr.aria-pressed]="alertesSeules()"
              (click)="alertesSeules.set(!alertesSeules())"
            >
              Alertes seulement ({{ l.totaux.auDela150 }})
            </button>
          </span>
        </div>
        <data-table
          [columns]="colonnes"
          [rows]="lignes()"
          trackKey="matricule"
          searchPlaceholder="Rechercher un salarié, un matricule, un département…"
          emptyText="Aucun salarié à afficher pour cette semaine."
        >
          <ng-template dataTableCell="salarie" let-row>
            <ng-container *ngTemplateOutlet="salarieCell; context: { $implicit: row }"></ng-container>
          </ng-template>
          <ng-template dataTableCell="hsAtterrissage" let-row>
            <strong>{{ heures(ligne(row).hsAtterrissage) }}</strong>
            <small class="pred-sub">{{ detail(ligne(row)) }}</small>
          </ng-template>
          <ng-template dataTableCell="niveau" let-row>
            <soft-badge [variant]="niveauVariant(ligne(row).niveau)">{{ niveauLabel(ligne(row)) }}</soft-badge>
            <small class="pred-sub">{{ action(ligne(row)) }}</small>
          </ng-template>
        </data-table>
      </soft-card>
    }

    <ng-template #salarieCell let-row>
      <strong>{{ row['nom'] }} {{ row['prenom'] }}</strong>
      <small class="pred-sub">{{ row['matricule'] }}</small>
    </ng-template>
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
      .pred-alert--warn {
        background: var(--warning-50, #fffbeb);
        color: var(--warning-700, #b45309);
        border: 1px solid var(--warning-200, #fde68a);
      }
      .pred-filters {
        display: inline-flex;
        flex-wrap: wrap;
        gap: 8px;
      }
      .pred-filter--off {
        border-color: var(--app-border);
        background: none;
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

  readonly landing = signal<WeekLanding | null>(null);
  readonly horizon = signal<WeekLandingWeek[]>([]);
  readonly loading = signal(false);
  readonly weekLoading = signal(false);
  /** Lundi (AAAA-MM-JJ) de la semaine analysée ; null : semaine en cours. */
  readonly semaine = signal<string | null>(null);
  readonly error = signal<string | null>(null);
  /** Département sélectionné dans la vue agrégée ; filtre la liste des salariés. */
  readonly departementFiltre = signal<string | null>(null);
  /** N'afficher que les salariés qui passent à 150 % ou au-delà. */
  readonly alertesSeules = signal(false);

  private readonly toutes = computed<WeekLandingRow[]>(() => this.landing()?.lignes ?? []);

  // Le texte des colonnes (format) sert aussi à la recherche du tableau partagé.
  readonly lignes = computed(() => {
    const filtre = this.departementFiltre();
    const lignes = this.toutes()
      .filter((l) => !filtre || this.nomDepartement(l) === filtre)
      .filter((l) => !this.alertesSeules() || l.niveau >= NIVEAU_ALERTE);
    return lignes as unknown as Record<string, unknown>[];
  });

  readonly colonnes: Column[] = [
    { key: 'salarie', label: 'Salarié', format: (_, row) => this.salarieTexte(row) },
    { key: 'departement', label: 'Département', format: (v) => (v as string | null) || '—' },
    {
      key: 'heuresRealisees',
      label: 'Fait',
      format: (v, row) => `${this.heures(v as number)} / ${this.heures(this.ligne(row).heuresContrat)}`,
    },
    { key: 'hsAcquises', label: 'HS acquises', format: (v) => this.heures(v as number) },
    { key: 'heuresPlanifieesRestantes', label: 'Reste planifié', format: (v, row) => this.resteTexte(v as number, this.ligne(row)) },
    { key: 'hsAtterrissage', label: 'Atterrissage HS', format: (v) => this.heures(v as number) },
    { key: 'niveau', label: 'Alerte', format: (_, row) => `${this.niveauLabel(this.ligne(row))} ${this.action(this.ligne(row))}` },
  ];

  readonly departements = computed<HsDepartement[]>(() => {
    const total = this.toutes().reduce((sum, l) => sum + l.hsAtterrissage, 0);
    const groupes = new Map<string, HsDepartement>();
    for (const l of this.toutes()) {
      const nom = this.nomDepartement(l);
      const d = groupes.get(nom) ?? { nom, salaries: 0, alertes: 0, acquises: 0, heures: 0, part: 0 };
      d.salaries++;
      if (l.niveau >= NIVEAU_ALERTE) d.alertes++;
      d.acquises += l.hsAcquises;
      d.heures += l.hsAtterrissage;
      groupes.set(nom, d);
    }
    return [...groupes.values()]
      .map((d) => ({ ...d, part: total > 0 ? d.heures / total : 0 }))
      .sort((a, b) => b.heures - a.heures || a.nom.localeCompare(b.nom));
  });

  /** Lundi de la semaine affichée : choix de l'utilisateur, sinon celle renvoyée par le serveur. */
  readonly semaineAffichee = computed(() => this.semaine() ?? this.jourIso(this.landing()?.totaux.semaineDebut));
  readonly semaineLabel = computed(() => {
    const semaine = this.semaineAffichee();
    return semaine ? `semaine du ${this.jourCourt(semaine)}` : 'semaine en cours';
  });
  private readonly horizonMax = computed(() => Math.max(0, ...this.horizon().map((w) => w.hsAtterrissage)));

  /**
   * Ce sur quoi repose le calcul : jusqu'où les pointages sont traités, et donc à partir de quel
   * jour la semaine est projetée d'après le planning. Un import en retard est signalé.
   */
  readonly fraicheur = computed<{ etat: 'pret' | 'baseline'; titre: string; detail: string }>(() => {
    const l = this.landing();
    const dernier = this.jourIso(l?.dernierJourPointe);
    if (!l || !dernier) {
      return {
        etat: 'baseline',
        titre: 'Aucun pointage traité',
        detail: "Toute la semaine est projetée d'après le planning.",
      };
    }
    const retard = this.joursEntre(dernier, this.jourIso(l.aujourdhui) ?? dernier);
    return {
      etat: retard > 1 ? 'baseline' : 'pret',
      titre: `Pointages traités jusqu'au ${this.jourCourt(dernier)}`,
      detail:
        retard > 1
          ? `Import en retard de ${retard} jours : les jours suivants sont projetés d'après le planning, pas comptés absents.`
          : "Les jours suivants sont projetés d'après le planning.",
    };
  });

  ngOnInit(): void {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set(null);
    forkJoin({
      landing: this.api.landing(this.semaine() ?? undefined),
      horizon: this.api.horizon(),
    }).subscribe({
      next: (result) => {
        this.horizon.set(result.horizon);
        this.appliquerSemaine(result.landing);
        this.loading.set(false);
      },
      error: (err) => {
        this.error.set(err?.error?.message ?? "Impossible de calculer l'atterrissage.");
        this.loading.set(false);
      },
    });
  }

  /** Charge l'atterrissage d'une semaine de l'horizon. */
  choisirSemaine(semaineDebut: string): void {
    const lundi = this.jourIso(semaineDebut);
    if (!lundi || lundi === this.semaineAffichee()) return;
    this.semaine.set(lundi);
    this.weekLoading.set(true);
    this.api.landing(lundi).subscribe({
      next: (landing) => {
        this.appliquerSemaine(landing);
        this.weekLoading.set(false);
      },
      error: (err) => {
        this.error.set(err?.error?.message ?? 'Impossible de charger la semaine.');
        this.weekLoading.set(false);
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

  private appliquerSemaine(landing: WeekLanding): void {
    this.landing.set(landing);
    // Un filtre sur un département absent de la semaine viderait la liste sans raison visible.
    if (!landing.lignes.some((l) => this.nomDepartement(l) === this.departementFiltre())) {
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

  niveauVariant(niveau: number): 'danger' | 'warning' | 'success' | 'muted' {
    if (niveau >= 3) return 'danger';
    if (niveau >= NIVEAU_ALERTE) return 'warning';
    return niveau >= 1 ? 'muted' : 'success';
  }

  /** Texte du niveau, pour que la recherche trouve « 150 % », « imposable » ou « aucune ». */
  niveauLabel(l: WeekLandingRow): string {
    if (l.niveau >= 3) return 'Imposable';
    if (l.niveau >= NIVEAU_ALERTE) return 'Passe à 150 %';
    return l.niveau >= 1 ? 'HS à 130 %' : 'Aucune HS';
  }

  /** Ce qu'il reste possible de faire : le seuil est déjà franchi, ou des heures peuvent être retirées. */
  action(l: WeekLandingRow): string {
    if (l.niveau < NIVEAU_ALERTE) return '';
    if (l.dejaFranchi) return 'seuil déjà franchi';
    return l.heuresARetirer > 0 ? `retirer ${this.heures(l.heuresARetirer)} du planning` : '';
  }

  /** Décomposition de l'atterrissage : ce que dit le planning, puis l'écart habituel du salarié. */
  detail(l: WeekLandingRow): string {
    if (l.semainesDerive === 0 || Math.abs(l.derive) < 0.05) return `planning seul : ${this.heures(l.hsSelonPlanning)}`;
    const signe = l.derive > 0 ? '+' : '−';
    return `planning ${this.heures(l.hsSelonPlanning)}, écart habituel ${signe}${this.heures(Math.abs(l.derive))}`;
  }

  private resteTexte(reste: number, l: WeekLandingRow): string {
    const sans = l.joursSansPlanning > 0 ? ` · ${l.joursSansPlanning} j sans planning` : '';
    return `${this.heures(reste)}${sans}`;
  }

  // Ligne du data-table, typée pour les modèles de cellule.
  ligne(row: Record<string, unknown>): WeekLandingRow {
    return row as unknown as WeekLandingRow;
  }

  private nomDepartement(l: WeekLandingRow): string {
    return l.departement?.trim() || SANS_DEPARTEMENT;
  }

  private salarieTexte(row: Record<string, unknown>): string {
    return [row['nom'], row['prenom'], row['matricule']].filter(Boolean).join(' ');
  }

  private jourIso(value: string | null | undefined): string | null {
    return value ? String(value).slice(0, 10) : null;
  }

  /** Nombre de jours entre deux dates AAAA-MM-JJ (calcul en UTC, sans effet d'heure d'été). */
  private joursEntre(debut: string, fin: string): number {
    return Math.round((Date.parse(`${fin}T00:00:00Z`) - Date.parse(`${debut}T00:00:00Z`)) / 86_400_000);
  }
}
