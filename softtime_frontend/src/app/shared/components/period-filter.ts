import {
  ChangeDetectionStrategy,
  Component,
  inject,
  input,
  OnInit,
  output,
  signal,
} from '@angular/core';
import { FormsModule } from '@angular/forms';
import { SoftSelect } from './soft-select';
import { SoftButton } from './soft-button';
import { PeriodRequest } from '../models';
import { monthStart, today, currentMonday, currentSunday } from '../utils/date';
import { SimpleChanges } from '@angular/core';
import { DeepLinkFilter, matchMatricule } from '../utils/deep-link';
import { CardPaieService, DepartementServiceService } from '../services/catalog.service';

@Component({
  selector: 'period-filter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, SoftSelect, SoftButton],
  template: `
    <div class="soft-toolbar">
      <label class="soft-field">
        <span class="soft-field__label">Du</span>
        <input class="soft-input" type="date" [(ngModel)]="from" (ngModelChange)="changed.emit()" name="from" />
      </label>
      <label class="soft-field">
        <span class="soft-field__label">Au</span>
        <input class="soft-input" type="date" [(ngModel)]="to" (ngModelChange)="changed.emit()" name="to" />
      </label>
      @if (showMatricule()) {
        <soft-select
          label="Matricule début"
          placeholder="Tous"
          [(ngModel)]="matriculeFrom"
          (ngModelChange)="changed.emit()"
          name="matriculeFrom"
          [options]="matriculeOptions()"
        ></soft-select>
        <soft-select
          label="Matricule fin"
          placeholder="Tous"
          [(ngModel)]="matriculeTo"
          (ngModelChange)="changed.emit()"
          name="matriculeTo"
          [options]="matriculeOptions()"
        ></soft-select>
      }
      @if (showDepartement()) {
        <soft-select
          label="Département"
          placeholder="Tous"
          [(ngModel)]="departement"
          (ngModelChange)="changed.emit()"
          name="departement"
          [options]="departementOptions()"
        ></soft-select>
      }
      <soft-button [loading]="busy()" [attr.aria-busy]="busy() || null" (click)="emit()">{{ searchLabel() }}</soft-button>
    </div>
  `,
})
export class PeriodFilter implements OnInit {
  private readonly cards = inject(CardPaieService);
  private readonly deptSvc = inject(DepartementServiceService);

  readonly showMatricule = input(true);
  readonly showDepartement = input(false);
  readonly autoSearch = input(false);
  readonly mondaySundayWeek = input(false);
  readonly searchLabel = input('Rechercher');
  /** Filtre issu d'un lien profond (déjà validé par readDeepLink) ; null : comportement habituel. */
  readonly initial = input<DeepLinkFilter | null>(null);
  /** Recherche en cours : le bouton est désactivé (avec son indicateur) ; faux par défaut, sans effet pour les autres écrans. */
  readonly busy = input(false);

  from = monthStart();
  to = today();
  matriculeFrom: string | null = null;
  matriculeTo: string | null = null;
  departement: string | null = null;

  readonly matriculeOptions = signal<{ value: string; label: string }[]>([]);
  readonly departementOptions = signal<{ value: string; label: string }[]>([]);
  readonly search = output<PeriodRequest>();
  /** Émise quand l'utilisateur modifie un champ du filtre (jamais pour un remplissage programmatique). */
  readonly changed = output<void>();

  ngOnInit(): void {
    if (this.mondaySundayWeek()) {
      this.from = currentMonday();
      this.to = currentSunday();
    }
    this.applyInitial();
    if (this.autoSearch()) this.emit();
    this.cards.list().subscribe({
      next: (list) => {
        const mats = [
          ...new Set(list.map((c) => c.sageMatricule).filter((m): m is string => !!m?.trim())),
        ].sort((a, b) => a.localeCompare(b, 'fr', { numeric: true }));
        this.matriculeOptions.set(
          mats.map((m) => {
            const card = list.find((c) => c.sageMatricule === m);
            const name = [card?.sageNom, card?.sagePrenom].filter(Boolean).join(' ');
            return { value: m, label: name ? `${m} — ${name}` : m };
          }),
        );
        this.resolveInitialMatricule(mats.map((value) => ({ value })));
        const rawDepts = list.map((c) => c.departement).filter((d): d is string => !!d?.trim());
        const depts = [...new Set(rawDepts)].sort((a, b) => a.localeCompare(b, 'fr'));
        this.departementOptions.set(depts.map((d) => ({ value: d, label: d })));
        if (depts.length === 0 && mats.length > 0) {
          this.deptSvc.lookupBatch(mats).subscribe({
            next: (results) => {
              const fromLookup = [
                ...new Set(results.map((r) => r.departement).filter((d): d is string => !!d?.trim())),
              ].sort((a, b) => a.localeCompare(b, 'fr'));
              if (fromLookup.length > 0) {
                this.departementOptions.set(fromLookup.map((d) => ({ value: d, label: d })));
              }
            },
          });
        }
      },
      error: () => {
        this.matriculeOptions.set([]);
        this.departementOptions.set([]);
        this.resolveInitialMatricule([]);
      },
    });
  }

  private pendingInitialMatricule: string | null = null;
  private optionsLoaded = false;

  /** Un nouveau lien profond vers la même page (composant réutilisé) : refait le pré-remplissage. */
  ngOnChanges(changes: SimpleChanges): void {
    const change = changes['initial'];
    if (change && !change.firstChange) this.applyInitial();
  }

  /** Remplit la période puis lance une seule recherche ; avec matricule, elle attend le chargement des options. */
  private applyInitial(): void {
    const init = this.initial();
    if (!init) return;
    this.from = init.from;
    this.to = init.to;
    this.pendingInitialMatricule = init.matricule;
    if (!init.matricule) {
      this.matriculeFrom = null;
      this.matriculeTo = null;
      this.emit();
    } else if (this.optionsLoaded) {
      this.resolveInitialMatricule(this.matriculeOptions());
    }
  }

  private resolveInitialMatricule(options: readonly { value: string }[]): void {
    this.optionsLoaded = true;
    const cle = this.pendingInitialMatricule;
    if (cle === null) return;
    this.pendingInitialMatricule = null;
    const found = matchMatricule(cle, options);
    this.matriculeFrom = found;
    this.matriculeTo = found;
    this.emit();
    // Sans liste visible, le matricule n'a servi qu'à cette émission : il ne doit pas coller aux recherches suivantes.
    if (!this.showMatricule()) {
      this.matriculeFrom = null;
      this.matriculeTo = null;
    }
  }

  emit(): void {
    this.search.emit({
      from: this.from,
      to: this.to,
      matriculeFrom: this.matriculeFrom || null,
      matriculeTo: this.matriculeTo || null,
      departement: this.departement || null,
    });
  }
}
