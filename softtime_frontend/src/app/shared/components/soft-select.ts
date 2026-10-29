import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  forwardRef,
  input,
  signal,
  viewChild,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { matchesSearch } from '../utils/search';

export interface SelectOption {
  value: string | number | null;
  label: string;
}

let uid = 0;

@Component({
  selector: 'soft-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => SoftSelect), multi: true },
  ],
  template: `
    <div class="soft-field" [class.soft-field--error]="!!error()">
      @if (label()) {
        <label class="soft-field__label" [attr.for]="id">
          {{ label() }}@if (required()) {<span class="soft-field__req" aria-hidden="true">*</span>}
        </label>
      }
      <div class="soft-select" [class.soft-select--filterable]="showFilter()">
        @if (showFilter()) {
          <input
            class="soft-input soft-select__filter"
            type="search"
            [value]="filterText()"
            [placeholder]="filterPlaceholder()"
            [attr.aria-label]="filterPlaceholder()"
            [attr.aria-controls]="id"
            (input)="onFilterInput($event)"
            (keydown)="onFilterKeydown($event)"
          />
        }
        <div class="soft-select__control">
          <select
            #select
            [id]="id"
            [disabled]="disabled()"
            [required]="required()"
            [attr.aria-required]="required() ? 'true' : null"
            [attr.aria-invalid]="error() ? 'true' : null"
            [attr.aria-describedby]="describedBy()"
            [value]="value()"
            (change)="onSelect($event)"
            (blur)="onTouched()"
          >
            @if (placeholder()) {
              <option value="">{{ placeholder() }}</option>
            }
            @for (opt of filteredOptions(); track opt.value) {
              <option [value]="opt.value">{{ opt.label }}</option>
            }
            @if (filterText() && filteredOptions().length === 0) {
              <option value="" disabled>Aucun résultat</option>
            }
          </select>
        </div>
      </div>
      @if (hint() && !error()) {
        <span class="soft-field__hint" [id]="hintId">{{ hint() }}</span>
      }
      @if (error()) {
        <span class="soft-field__error" [id]="errorId">{{ error() }}</span>
      }
    </div>
  `,
})
export class SoftSelect implements ControlValueAccessor {
  readonly id = `soft-select-${++uid}`;
  readonly hintId = `${this.id}-hint`;
  readonly errorId = `${this.id}-error`;
  readonly label = input('');
  readonly placeholder = input('');
  readonly required = input(false);
  readonly hint = input('');
  readonly error = input('');
  readonly options = input<SelectOption[]>([]);
  /** Affiche un champ de filtre. `auto` ne l'affiche que pour les longues listes. */
  readonly filterable = input<boolean | 'auto'>('auto');
  readonly filterThreshold = input(8);
  readonly filterPlaceholder = input('Filtrer les options…');

  readonly describedBy = computed(() => {
    if (this.error()) return this.errorId;
    if (this.hint()) return this.hintId;
    return null;
  });

  readonly value = signal<string>('');
  readonly disabled = signal(false);
  readonly filterText = signal('');

  private readonly selectEl = viewChild<ElementRef<HTMLSelectElement>>('select');

  readonly showFilter = computed(
    () =>
      !this.disabled() &&
      (this.filterable() === true ||
        (this.filterable() === 'auto' && this.options().length >= this.filterThreshold())),
  );

  readonly filteredOptions = computed<SelectOption[]>(() => {
    const options = this.options();
    const query = this.filterText().trim();
    if (!query) return options;

    const filtered = options.filter((option) => matchesSearch(query, [option.label]));
    const selected = options.find((option) => String(option.value) === this.value());
    // La valeur sélectionnée reste visible même si elle ne correspond pas au filtre.
    if (selected && !filtered.includes(selected)) filtered.unshift(selected);
    return filtered;
  });

  private onChange: (v: unknown) => void = () => {};
  onTouched: () => void = () => {};

  onSelect(event: Event): void {
    const raw = (event.target as HTMLSelectElement).value;
    this.value.set(raw);
    this.filterText.set('');
    const match = this.options().find((o) => String(o.value) === raw);
    this.onChange(match ? match.value : raw === '' ? null : raw);
  }

  onFilterInput(event: Event): void {
    this.filterText.set((event.target as HTMLInputElement).value);
  }

  onFilterKeydown(event: KeyboardEvent): void {
    if (event.key === 'Enter') {
      event.preventDefault();
      this.selectEl()?.nativeElement.focus();
      return;
    }
    if (event.key === 'Escape') {
      event.preventDefault();
      this.filterText.set('');
    }
  }

  writeValue(v: unknown): void {
    const next = v === null || v === undefined ? '' : String(v);
    if (next !== this.value()) this.filterText.set('');
    this.value.set(next);
  }
  registerOnChange(fn: (v: unknown) => void): void {
    this.onChange = fn;
  }
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }
  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }
}
