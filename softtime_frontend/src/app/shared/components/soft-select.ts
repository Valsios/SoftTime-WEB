import {
  ChangeDetectionStrategy,
  Component,
  computed,
  ElementRef,
  forwardRef,
  HostListener,
  inject,
  input,
  signal,
  viewChild,
  viewChildren,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { Icon } from './icon';
import { matchesSearch, normalizeSearchText } from '../utils/search';

export interface SelectOption {
  value: string | number | null;
  label: string;
}

let uid = 0;

@Component({
  selector: 'soft-select',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
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

      @if (showFilter()) {
        <div class="soft-combobox">
          <div #comboboxControl class="soft-combobox__control">
            <input
              #comboboxInput
              class="soft-input soft-combobox__input"
              type="text"
              role="combobox"
              autocomplete="off"
              spellcheck="false"
              [id]="id"
              [value]="inputValue()"
              [placeholder]="placeholder() || filterPlaceholder()"
              [disabled]="disabled()"
              [required]="required()"
              [attr.aria-required]="required() ? 'true' : null"
              [attr.aria-invalid]="error() ? 'true' : null"
              [attr.aria-describedby]="describedBy()"
              [attr.aria-expanded]="open()"
              [attr.aria-controls]="listboxId"
              [attr.aria-autocomplete]="'list'"
              [attr.aria-haspopup]="'listbox'"
              [attr.aria-activedescendant]="activeDescendant()"
              (focus)="onFocus()"
              (click)="onInputClick()"
              (input)="onInput($event)"
              (keydown)="onKeydown($event)"
              (blur)="onBlur()"
            />
            @if (value() && !required() && !disabled()) {
              <button
                type="button"
                class="soft-combobox__clear"
                (pointerdown)="onControlPointerDown()"
                (mousedown)="$event.preventDefault()"
                (click)="clearValue()"
                aria-label="Effacer la sélection"
              >
                &times;
              </button>
            }
            <button
              type="button"
              class="soft-combobox__chevron"
              [disabled]="disabled()"
              tabindex="-1"
              (pointerdown)="onControlPointerDown()"
              (mousedown)="$event.preventDefault()"
              (click)="toggleOpen()"
              [attr.aria-label]="open() ? 'Fermer la liste' : 'Ouvrir la liste'"
              [attr.aria-expanded]="open()"
            >
              <app-icon [name]="open() ? 'chevron-up' : 'chevron-down'" [size]="16"></app-icon>
            </button>
          </div>

          @if (open()) {
            <div
              class="soft-combobox__panel"
              [class.is-drop-up]="dropUp()"
              role="listbox"
              [id]="listboxId"
              (pointerdown)="onControlPointerDown()"
            >
              @for (opt of filteredOptions(); track opt.value; let i = $index) {
                <div
                  #comboboxOption
                  class="soft-combobox__option"
                  role="option"
                  tabindex="-1"
                  [id]="optionId(i)"
                  [class.is-active]="i === activeIndex()"
                  [class.is-selected]="isSelected(opt)"
                  [attr.aria-selected]="isSelected(opt)"
                  (pointerdown)="onControlPointerDown()"
                  (mousedown)="$event.preventDefault()"
                  (mouseenter)="activeIndex.set(i)"
                  (click)="selectOption(opt)"
                >
                  <span>{{ opt.label }}</span>
                  @if (isSelected(opt)) {
                    <span class="soft-combobox__check" aria-hidden="true">
                      <app-icon name="check" [size]="14"></app-icon>
                    </span>
                  }
                </div>
              } @empty {
                <div class="soft-combobox__empty">Aucun résultat</div>
              }
            </div>
          }
        </div>
      } @else {
        <div class="soft-select">
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
              @for (opt of options(); track opt.value) {
                <option [value]="opt.value">{{ opt.label }}</option>
              }
            </select>
          </div>
        </div>
      }

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
  private readonly host = inject(ElementRef<HTMLElement>);
  readonly id = `soft-select-${++uid}`;
  readonly listboxId = `${this.id}-listbox`;
  readonly hintId = `${this.id}-hint`;
  readonly errorId = `${this.id}-error`;
  readonly label = input('');
  readonly placeholder = input('');
  readonly required = input(false);
  readonly hint = input('');
  readonly error = input('');
  readonly options = input<SelectOption[]>([]);
  /** Affiche un combobox filtrable. `auto` ne l'active que pour les longues listes. */
  readonly filterable = input<boolean | 'auto'>('auto');
  readonly filterThreshold = input(8);
  readonly filterPlaceholder = input('Rechercher…');

  private readonly inputEl = viewChild<ElementRef<HTMLInputElement>>('comboboxInput');
  private readonly controlEl = viewChild<ElementRef<HTMLElement>>('comboboxControl');
  private readonly optionEls = viewChildren<ElementRef<HTMLElement>>('comboboxOption');

  readonly value = signal<string>('');
  readonly disabled = signal(false);
  readonly open = signal(false);
  readonly query = signal('');
  readonly inputText = signal('');
  readonly activeIndex = signal(-1);
  readonly dropUp = signal(false);

  readonly describedBy = computed(() => {
    if (this.error()) return this.errorId;
    if (this.hint()) return this.hintId;
    return null;
  });

  readonly showFilter = computed(
    () =>
      !this.disabled() &&
      (this.filterable() === true ||
        (this.filterable() === 'auto' && this.options().length >= this.filterThreshold())),
  );

  readonly selectedOption = computed<SelectOption | null>(() => {
    const current = this.value();
    if (!current) return null;
    return this.options().find((option) => String(option.value) === current) ?? null;
  });

  readonly displayLabel = computed(() => this.selectedOption()?.label ?? '');

  readonly inputValue = computed(() => (this.open() ? this.inputText() : this.displayLabel()));

  readonly filteredOptions = computed<SelectOption[]>(() => {
    const query = this.query().trim();
    if (!query) return this.options();
    return this.options().filter((option) => matchesSearch(query, [option.label]));
  });

  readonly activeDescendant = computed(() => {
    if (!this.open()) return null;
    const index = this.activeIndex();
    return index >= 0 && index < this.filteredOptions().length ? this.optionId(index) : null;
  });

  private onChange: (v: unknown) => void = () => {};
  onTouched: () => void = () => {};
  private pointerDownInside = false;
  private pointerResetTimer: ReturnType<typeof setTimeout> | null = null;

  onControlPointerDown(): void {
    this.pointerDownInside = true;
    if (this.pointerResetTimer !== null) clearTimeout(this.pointerResetTimer);
    this.pointerResetTimer = setTimeout(() => {
      this.pointerDownInside = false;
      this.pointerResetTimer = null;
    }, 250);
  }

  @HostListener('document:pointerdown', ['$event'])
  onDocumentPointerDown(event: PointerEvent): void {
    if (!this.open()) return;
    const target = event.target as Node | null;
    if (target && !this.host.nativeElement.contains(target)) this.closeAndRestore();
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    if (this.open()) this.updateDropUp();
  }

  onFocus(): void {
    if (this.open()) return;
    queueMicrotask(() => {
      const input = this.inputEl()?.nativeElement;
      if (input && document.activeElement === input) input.select();
    });
  }

  onInputClick(): void {
    if (!this.open()) {
      this.openList();
      return;
    }
    if (!this.query().trim()) this.inputEl()?.nativeElement.select();
  }

  onInput(event: Event): void {
    const text = (event.target as HTMLInputElement).value;
    this.inputText.set(text);
    this.query.set(text);
    if (!this.open()) {
      this.open.set(true);
      this.updateDropUp();
    }
    this.activeIndex.set(this.filteredOptions().length ? 0 : -1);
  }

  onKeydown(event: KeyboardEvent): void {
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.moveActive(1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.moveActive(-1);
        break;
      case 'Enter':
        event.preventDefault();
        this.selectActive();
        break;
      case 'Escape':
        if (this.open()) {
          event.preventDefault();
          event.stopPropagation();
          this.closeAndRestore();
        }
        break;
      case 'Tab':
        this.closeAndRestore();
        break;
    }
  }

  onBlur(): void {
    this.onTouched();
    if (this.pointerDownInside) return;
    this.closeAndRestore();
  }

  onSelect(event: Event): void {
    const raw = (event.target as HTMLSelectElement).value;
    this.value.set(raw);
    const match = this.options().find((o) => String(o.value) === raw);
    this.onChange(match ? match.value : raw === '' ? null : raw);
  }

  selectOption(option: SelectOption): void {
    this.value.set(option.value === null || option.value === undefined ? '' : String(option.value));
    this.inputText.set(option.label);
    this.query.set('');
    this.open.set(false);
    this.onChange(option.value);
    this.onTouched();
    queueMicrotask(() => {
      const input = this.inputEl()?.nativeElement;
      if (input && document.activeElement === input) input.select();
    });
  }

  clearValue(): void {
    this.value.set('');
    this.inputText.set('');
    this.query.set('');
    this.open.set(false);
    this.onChange(null);
    this.onTouched();
    queueMicrotask(() => this.inputEl()?.nativeElement.focus());
  }

  toggleOpen(): void {
    if (this.disabled()) return;
    if (this.open()) this.closeAndRestore();
    else this.openList();
  }

  isSelected(option: SelectOption): boolean {
    return String(option.value ?? '') === this.value();
  }

  optionId(index: number): string {
    return `${this.id}-option-${index}`;
  }

  private openList(): void {
    if (this.disabled() || this.open()) return;
    this.open.set(true);
    this.query.set('');
    this.inputText.set(this.displayLabel());
    const selectedIndex = this.options().findIndex((option) => String(option.value) === this.value());
    this.activeIndex.set(selectedIndex >= 0 ? selectedIndex : this.options().length ? 0 : -1);
    this.updateDropUp();
    queueMicrotask(() => {
      const input = this.inputEl()?.nativeElement;
      input?.focus();
      input?.select();
      this.optionEls()[this.activeIndex()]?.nativeElement.scrollIntoView({ block: 'nearest' });
    });
  }

  private closeAndRestore(): void {
    if (!this.open()) return;
    this.open.set(false);
    this.query.set('');
    this.inputText.set(this.displayLabel());
  }

  private moveActive(delta: number): void {
    if (this.disabled()) return;
    if (!this.open()) {
      this.openList();
      return;
    }
    const count = this.filteredOptions().length;
    if (!count) return;
    const currentRaw = this.activeIndex();
    const current = currentRaw >= 0 && currentRaw < count ? currentRaw : -1;
    const next = current < 0 ? (delta > 0 ? 0 : count - 1) : (current + delta + count) % count;
    this.activeIndex.set(next);
    this.scrollActiveIntoView();
  }

  private selectActive(): void {
    if (!this.open()) {
      this.openList();
      return;
    }
    const options = this.filteredOptions();
    const query = normalizeSearchText(this.query());
    if (query) {
      const exact = this.options().find((candidate) => normalizeSearchText(candidate.label) === query);
      if (exact) {
        this.selectOption(exact);
        return;
      }
    }
    if (options.length) {
      const index = Math.min(Math.max(this.activeIndex(), 0), options.length - 1);
      const option = options[index];
      if (option) {
        this.selectOption(option);
        return;
      }
    }
    this.closeAndRestore();
  }

  private scrollActiveIntoView(): void {
    queueMicrotask(() => {
      this.optionEls()[this.activeIndex()]?.nativeElement.scrollIntoView({ block: 'nearest' });
    });
  }

  private updateDropUp(): void {
    const control = this.controlEl()?.nativeElement;
    if (!control) return;
    const rect = control.getBoundingClientRect();
    const spaceBelow = window.innerHeight - rect.bottom;
    const spaceAbove = rect.top;
    this.dropUp.set(spaceBelow < 260 && spaceAbove > spaceBelow);
  }

  writeValue(v: unknown): void {
    const next = v === null || v === undefined ? '' : String(v);
    this.value.set(next);
    this.open.set(false);
    this.query.set('');
    this.inputText.set(this.displayLabel());
  }
  registerOnChange(fn: (v: unknown) => void): void {
    this.onChange = fn;
  }
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }
  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
    if (isDisabled) this.closeAndRestore();
  }
}
