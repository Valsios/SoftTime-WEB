import { ChangeDetectionStrategy, Component, computed, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { Icon } from './icon';

let uid = 0;

@Component({
  selector: 'soft-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [Icon],
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => SoftInput), multi: true },
  ],
  template: `
    <div class="soft-field" [class.soft-field--error]="!!error()">
      @if (label()) {
        <label class="soft-field__label" [attr.for]="id">
          {{ label() }}@if (required()) {<span class="soft-field__req" aria-hidden="true">*</span>}
        </label>
      }
      <div class="soft-field__input">
        <input
          [id]="id"
          class="soft-input"
          [type]="inputType()"
          [placeholder]="placeholder()"
          [required]="required()"
          [disabled]="disabled()"
          [attr.aria-required]="required() ? 'true' : null"
          [attr.aria-invalid]="error() ? 'true' : null"
          [attr.aria-describedby]="describedBy()"
          [value]="value()"
          (input)="onInput($event)"
          (blur)="onTouched()"
        />
        @if (type() === 'password') {
          <button
            type="button"
            class="soft-field__reveal"
            [attr.aria-label]="revealed() ? 'Masquer le mot de passe' : 'Afficher le mot de passe'"
            [attr.aria-pressed]="revealed()"
            [attr.aria-controls]="id"
            (click)="toggleReveal()"
          >
            <app-icon [name]="revealed() ? 'eye-off' : 'eye'" [size]="16"></app-icon>
          </button>
        }
      </div>
      @if (hint() && !error()) {
        <span class="soft-field__hint" [id]="hintId">{{ hint() }}</span>
      }
      @if (error()) {
        <span class="soft-field__error" [id]="errorId">{{ error() }}</span>
      }
    </div>
  `,
  styles: [
    `
      .soft-field__input {
        position: relative;
      }
      .soft-field__input .soft-input {
        padding-right: 42px;
      }
      .soft-field__reveal {
        position: absolute;
        top: 50%;
        right: 6px;
        transform: translateY(-50%);
        display: inline-flex;
        align-items: center;
        justify-content: center;
        width: 32px;
        height: 32px;
        padding: 0;
        border: 0;
        border-radius: var(--radius-sm);
        background: transparent;
        color: var(--text-muted);
        cursor: pointer;
      }
      .soft-field__reveal:hover {
        color: var(--app-text);
        background: var(--surface-light);
      }
      .soft-field__reveal:focus-visible {
        outline: 2px solid var(--primary-500);
        outline-offset: 1px;
      }
    `,
  ],
})
export class SoftInput implements ControlValueAccessor {
  readonly id = `soft-input-${++uid}`;
  readonly hintId = `${this.id}-hint`;
  readonly errorId = `${this.id}-error`;
  readonly label = input('');
  readonly type = input<'text' | 'email' | 'password' | 'number' | 'date' | 'time'>('text');
  readonly placeholder = input('');
  readonly required = input(false);
  readonly hint = input('');
  readonly error = input('');

  readonly value = signal<string>('');
  readonly disabled = signal(false);
  readonly revealed = signal(false);

  readonly describedBy = computed(() => {
    if (this.error()) return this.errorId;
    if (this.hint()) return this.hintId;
    return null;
  });

  readonly inputType = computed(() =>
    this.type() === 'password' && this.revealed() ? 'text' : this.type(),
  );

  private onChange: (v: unknown) => void = () => {};
  onTouched: () => void = () => {};

  onInput(event: Event): void {
    const raw = (event.target as HTMLInputElement).value;
    if (this.type() === 'password' && raw === '') this.revealed.set(false);
    this.value.set(raw);
    this.onChange(this.type() === 'number' ? (raw === '' ? null : Number(raw)) : raw);
  }

  toggleReveal(): void {
    this.revealed.update((revealed) => !revealed);
  }

  writeValue(v: unknown): void {
    const next = v === null || v === undefined ? '' : String(v);
    // Le modèle a été réinitialisé (ouverture d'un formulaire) : on masque à nouveau la saisie.
    if (next !== this.value()) this.revealed.set(false);
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
