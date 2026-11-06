import { ChangeDetectionStrategy, Component, computed, inject, OnDestroy, OnInit, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import { PageHeader, SoftCard } from '../../shared/components';
import {
  CARD_ERROR,
  dayText,
  EMPTY_ABSENCES,
  EMPTY_ANOMALIES,
  EMPTY_DELAYS,
  EMPTY_EXO_IMPO,
  EMPTY_WEEKS,
  hoursText,
  LOADING,
  minutesText,
  monthOptions,
  SCOPE_NOTE,
  timeText,
} from './self-space.logic';
import { describeSelfError, SelfErrorNotice, strongestNotice } from './self-space.errors';
import { SelfAbsences, SelfAnomalies, SelfDelays, SelfSpaceService, SelfWeeks } from './self-space.service';

interface CardState<T> {
  status: 'loading' | 'ok' | 'error';
  data: T | null;
}

const loading = <T>(): CardState<T> => ({ status: 'loading', data: null });

/**
 * « Mon espace » : les données du salarié connecté, en lecture seule, en texte (heures par semaine et EXO / IMPO, anomalies,
 * absences et congés, retards). Aucun lien vers un autre écran, aucun export. Le serveur déduit le salarié du jeton : la page
 * n'envoie que la période et chaque carte se charge (ou échoue) séparément.
 */
@Component({
  selector: 'app-self-space',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [PageHeader, SoftCard],
  template: `
    <page-header title="Mon espace" [subtitle]="scopeNote"></page-header>

    <label class="self-month">
      <span>Mois</span>
      <select class="soft-input" name="mois" (change)="onMonth($event)">
        @for (m of months; track m.value) {
          <option [value]="m.value" [selected]="m.value === month()">{{ m.label }}</option>
        }
      </select>
    </label>

    @if (notice(); as n) {
      <div class="self-notice" role="status" [attr.data-kind]="n.kind">
        <p class="self-notice__message">{{ n.message }}</p>
        <p class="self-notice__hint">{{ n.hint }}</p>
      </div>
    }

    @if (showCards()) {
    <soft-card variant="bordered" padding="sm">
      <section aria-labelledby="self-hours" [attr.aria-busy]="weeks().status === 'loading'">
        <h2 id="self-hours" class="self-title">Mes heures par semaine</h2>
        @switch (weeks().status) {
          @case ('loading') { <p class="self-muted" role="status">{{ loadingText }}</p> }
          @case ('error') { <p class="self-muted" role="alert">{{ errorText }}</p> }
          @default {
            @if (weeks().data; as w) {
              @if (w.semaines.length === 0) {
                <p class="self-muted">{{ emptyWeeks }}</p>
              } @else {
                <table class="soft-table">
                  <caption class="self-sr">Semaines validées</caption>
                  <thead>
                    <tr>
                      <th scope="col">Semaine du</th>
                      <th scope="col">Heures</th>
                      <th scope="col">Heures sup.</th>
                      <th scope="col">À 130 %</th>
                      <th scope="col">À 150 %</th>
                      <th scope="col">Nuit</th>
                      <th scope="col">Dimanche</th>
                      <th scope="col">Férié</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (s of w.semaines; track $index) {
                      <tr>
                        <th scope="row">{{ day(s.lundi) }} au {{ day(s.dimanche) }}</th>
                        <td>{{ hours(s.heuresTravaillees) }}</td>
                        <td>{{ hours(s.heuresSupplementaires) }}</td>
                        <td>{{ hours(s.hs130) }}</td>
                        <td>{{ hours(s.hs150) }}</td>
                        <td>{{ hours(s.nuit) }}</td>
                        <td>{{ hours(s.dimanches) }}</td>
                        <td>{{ hours(s.feries) }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              }
              <h3 class="self-subtitle">Heures supplémentaires exonérées et imposables</h3>
              @if (w.exoImpo.length === 0) {
                <p class="self-muted">{{ emptyExoImpo }}</p>
              } @else {
                <table class="soft-table">
                  <caption class="self-sr">Cumuls EXO / IMPO par période de calcul</caption>
                  <thead>
                    <tr>
                      <th scope="col">Période</th>
                      <th scope="col">Exonérées 130 %</th>
                      <th scope="col">Exonérées 150 %</th>
                      <th scope="col">Imposables 130 %</th>
                      <th scope="col">Imposables 150 %</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (e of w.exoImpo; track $index) {
                      <tr>
                        <th scope="row">{{ day(e.periodeDebut) }} au {{ day(e.periodeFin) }}</th>
                        <td>{{ hours(e.exo130) }}</td>
                        <td>{{ hours(e.exo150) }}</td>
                        <td>{{ hours(e.imp130) }}</td>
                        <td>{{ hours(e.imp150) }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              }
            }
          }
        }
      </section>
    </soft-card>

    <soft-card variant="bordered" padding="sm">
      <section aria-labelledby="self-anomalies" [attr.aria-busy]="anomalies().status === 'loading'">
        <h2 id="self-anomalies" class="self-title">Mes anomalies</h2>
        @switch (anomalies().status) {
          @case ('loading') { <p class="self-muted" role="status">{{ loadingText }}</p> }
          @case ('error') { <p class="self-muted" role="alert">{{ errorText }}</p> }
          @default {
            @if (anomalies().data; as a) {
              @if (a.anomalies.length === 0) {
                <p class="self-muted">{{ emptyAnomalies }}</p>
              } @else {
                <ul class="self-list">
                  @for (x of a.anomalies; track $index) {
                    <li>
                      <strong>{{ day(x.date) }}</strong> · {{ x.nature }}
                      @if (x.heure1) { · {{ time(x.heure1) }}@if (x.heure2) { – {{ time(x.heure2) }} } }
                      @if (x.ferie) { · jour férié }
                    </li>
                  }
                </ul>
              }
            }
          }
        }
      </section>
    </soft-card>

    <soft-card variant="bordered" padding="sm">
      <section aria-labelledby="self-absences" [attr.aria-busy]="absences().status === 'loading'">
        <h2 id="self-absences" class="self-title">Mes absences et congés</h2>
        @switch (absences().status) {
          @case ('loading') { <p class="self-muted" role="status">{{ loadingText }}</p> }
          @case ('error') { <p class="self-muted" role="alert">{{ errorText }}</p> }
          @default {
            @if (absences().data; as a) {
              @if (a.absences.length === 0) {
                <p class="self-muted">{{ emptyAbsences }}</p>
              } @else {
                <ul class="self-list">
                  @for (x of a.absences; track $index) {
                    <li>
                      <strong>{{ day(x.date) }}</strong> · {{ x.type }} · {{ x.periode }}
                      @if (x.dureeHeures !== null) { · {{ hours(x.dureeHeures) }} }
                      @if (x.motif) { · {{ x.motif }} }
                    </li>
                  }
                </ul>
              }
            }
          }
        }
      </section>
    </soft-card>

    <soft-card variant="bordered" padding="sm">
      <section aria-labelledby="self-delays" [attr.aria-busy]="delays().status === 'loading'">
        <h2 id="self-delays" class="self-title">Mes retards</h2>
        @switch (delays().status) {
          @case ('loading') { <p class="self-muted" role="status">{{ loadingText }}</p> }
          @case ('error') { <p class="self-muted" role="alert">{{ errorText }}</p> }
          @default {
            @if (delays().data; as d) {
              @if (d.retards.length === 0) {
                <p class="self-muted">{{ emptyDelays }}</p>
              } @else {
                <ul class="self-list">
                  @for (x of d.retards; track $index) {
                    <li>
                      <strong>{{ day(x.date) }}</strong> · prévu {{ time(x.heurePrevue) }}, arrivé {{ time(x.heureEntree) }}
                      · {{ minutes(x.minutesRetard) }} de retard (tolérance {{ minutes(x.toleranceMinutes) }})
                    </li>
                  }
                </ul>
              }
            }
          }
        }
      </section>
    </soft-card>
    }
  `,
  styles: [
    `
      :host { display: flex; flex-direction: column; gap: 16px; }
      :host ::ng-deep .soft-page-header { margin-bottom: 0; }
      .self-notice { padding: 14px 16px; border: 1px solid var(--danger-border); border-radius: var(--radius-md); background: var(--danger-bg); color: var(--danger); }
      .self-notice[data-kind='server'], .self-notice[data-kind='network'] { border-color: var(--warning-border); background: var(--warning-bg); color: var(--warning); }
      .self-notice__message { margin: 0; font-size: 14.5px; font-weight: 600; }
      .self-notice__hint { margin: 4px 0 0; font-size: 13.5px; }
      .self-month { display: inline-flex; align-items: center; gap: 8px; font-size: 14px; color: var(--text-secondary); }
      .self-title { margin: 0; padding: 8px 8px 4px; font-family: var(--font-heading); font-size: 16px; color: var(--app-text-strong); }
      .self-subtitle { margin: 16px 8px 4px; font-size: 14px; color: var(--app-text-strong); }
      .self-muted { margin: 0; padding: 8px; font-size: 13.5px; color: var(--text-secondary); }
      .self-list { margin: 0; padding: 4px 8px 8px 28px; display: grid; gap: 6px; font-size: 14px; color: var(--app-text); }
      .self-sr { position: absolute; width: 1px; height: 1px; padding: 0; margin: -1px; overflow: hidden; clip: rect(0, 0, 0, 0); white-space: nowrap; border: 0; }
      section { position: relative; }
    `,
  ],
})
export class SelfSpacePage implements OnInit, OnDestroy {
  private readonly api = inject(SelfSpaceService);

  readonly months = monthOptions();
  readonly month = signal(this.months[0].value);

  readonly weeks = signal<CardState<SelfWeeks>>(loading());
  readonly anomalies = signal<CardState<SelfAnomalies>>(loading());
  readonly absences = signal<CardState<SelfAbsences>>(loading());
  readonly delays = signal<CardState<SelfDelays>>(loading());

  /** Encadré durable de la page (erreur de api/me/space/*) ; vide tant qu'aucune lecture n'a échoué. */
  readonly notice = signal<SelfErrorNotice | null>(null);
  /** Une erreur de compte, de période ou de session remplace les cartes : « aucune donnée » serait trompeur. */
  readonly showCards = computed(() => !this.notice()?.masksCards);

  readonly scopeNote = SCOPE_NOTE;
  readonly loadingText = LOADING;
  readonly errorText = CARD_ERROR;
  readonly emptyWeeks = EMPTY_WEEKS;
  readonly emptyExoImpo = EMPTY_EXO_IMPO;
  readonly emptyAnomalies = EMPTY_ANOMALIES;
  readonly emptyAbsences = EMPTY_ABSENCES;
  readonly emptyDelays = EMPTY_DELAYS;

  readonly range = computed(() => {
    const m = this.months.find((x) => x.value === this.month()) ?? this.months[0];
    return { from: m.from, to: m.to };
  });

  private requests: Subscription[] = [];

  ngOnInit(): void {
    this.load();
  }

  ngOnDestroy(): void {
    this.cancel();
  }

  onMonth(event: Event): void {
    const value = (event.target as HTMLSelectElement).value;
    if (value === this.month()) return;
    this.month.set(value);
    this.load();
  }

  day = dayText;
  hours = hoursText;
  time = timeText;
  minutes = minutesText;

  private cancel(): void {
    this.requests.forEach((r) => r.unsubscribe());
    this.requests = [];
  }

  /** Quatre lectures indépendantes : une carte qui échoue n'empêche pas les autres ; changer de mois annule les précédentes. */
  private load(): void {
    this.cancel();
    const range = this.range();
    this.weeks.set(loading());
    this.anomalies.set(loading());
    this.absences.set(loading());
    this.delays.set(loading());
    this.notice.set(null);
    const track = <T>(state: { set(v: CardState<T>): void }) => ({
      next: (data: T) => state.set({ status: 'ok', data }),
      error: (err: unknown) => {
        state.set({ status: 'error', data: null });
        this.notice.update((courant) => strongestNotice(courant, describeSelfError(err)));
      },
    });
    this.requests = [
      this.api.weeks(range).subscribe(track(this.weeks)),
      this.api.anomalies(range).subscribe(track(this.anomalies)),
      this.api.absences(range).subscribe(track(this.absences)),
      this.api.delays(range).subscribe(track(this.delays)),
    ];
  }
}
