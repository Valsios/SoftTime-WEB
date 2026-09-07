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
import { CardPaieService } from '../services/catalog.service';

@Component({
  selector: 'period-filter',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [FormsModule, SoftSelect, SoftButton],
  template: `
    <div class="soft-toolbar">
      <label class="soft-field">
        <span class="soft-field__label">Du</span>
        <input class="soft-input" type="date" [(ngModel)]="from" name="from" />
      </label>
      <label class="soft-field">
        <span class="soft-field__label">Au</span>
        <input class="soft-input" type="date" [(ngModel)]="to" name="to" />
      </label>
      @if (showMatricule()) {
        <soft-select
          label="Matricule début"
          placeholder="Tous"
          [(ngModel)]="matriculeFrom"
          name="matriculeFrom"
          [options]="matriculeOptions()"
        ></soft-select>
        <soft-select
          label="Matricule fin"
          placeholder="Tous"
          [(ngModel)]="matriculeTo"
          name="matriculeTo"
          [options]="matriculeOptions()"
        ></soft-select>
      }
      @if (showBranche()) {
        <soft-select
          label="Branche"
          placeholder="Toutes"
          [(ngModel)]="branche"
          name="branche"
          [options]="brancheOptions()"
        ></soft-select>
      }
      <soft-button (click)="emit()">{{ searchLabel() }}</soft-button>
    </div>
  `,
})
export class PeriodFilter implements OnInit {
  private readonly cards = inject(CardPaieService);

  readonly showMatricule = input(true);
  readonly showBranche = input(false);
  readonly autoSearch = input(false);
  readonly mondaySundayWeek = input(false);
  readonly searchLabel = input('Rechercher');

  from = monthStart();
  to = today();
  matriculeFrom: string | null = null;
  matriculeTo: string | null = null;
  branche: string | null = null;

  readonly matriculeOptions = signal<{ value: string; label: string }[]>([]);
  readonly brancheOptions = signal<{ value: string; label: string }[]>([]);
  readonly search = output<PeriodRequest>();

  ngOnInit(): void {
    if (this.mondaySundayWeek()) {
      this.from = currentMonday();
      this.to = currentSunday();
    }
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
        const branches = [
          ...new Set(list.map((c) => c.branche).filter((b): b is string => !!b?.trim())),
        ].sort();
        this.brancheOptions.set(branches.map((b) => ({ value: b, label: b })));
      },
      error: () => {
        this.matriculeOptions.set([]);
      },
    });
  }

  emit(): void {
    this.search.emit({
      from: this.from,
      to: this.to,
      matriculeFrom: this.matriculeFrom || null,
      matriculeTo: this.matriculeTo || null,
      branche: this.branche || null,
    });
  }
}
