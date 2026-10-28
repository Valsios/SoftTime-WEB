import { ChangeDetectionStrategy, Component, computed, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

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
      <div class="soft-select">
        <select
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

  readonly describedBy = computed(() => {
    if (this.error()) return this.errorId;
    if (this.hint()) return this.hintId;
    return null;
  });

  readonly value = signal<string>('');
  readonly disabled = signal(false);

  private onChange: (v: unknown) => void = () => {};
  onTouched: () => void = () => {};

  onSelect(event: Event): void {
    const raw = (event.target as HTMLSelectElement).value;
    this.value.set(raw);
    const match = this.options().find((o) => String(o.value) === raw);
    this.onChange(match ? match.value : raw === '' ? null : raw);
  }

  writeValue(v: unknown): void {
    this.value.set(v === null || v === undefined ? '' : String(v));
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
