import { ChangeDetectionStrategy, Component, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

let uid = 0;

@Component({
  selector: 'soft-input',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => SoftInput), multi: true },
  ],
  template: `
    <div class="soft-field" [class.soft-field--error]="!!error()">
      @if (label()) {
        <label class="soft-field__label" [attr.for]="id">
          {{ label() }}@if (required()) {<span class="soft-field__req">*</span>}
        </label>
      }
      <input
        [id]="id"
        class="soft-input"
        [type]="type()"
        [placeholder]="placeholder()"
        [required]="required()"
        [disabled]="disabled()"
        [value]="value()"
        (input)="onInput($event)"
        (blur)="onTouched()"
      />
      @if (hint() && !error()) {
        <span class="soft-field__hint">{{ hint() }}</span>
      }
      @if (error()) {
        <span class="soft-field__error">{{ error() }}</span>
      }
    </div>
  `,
})
export class SoftInput implements ControlValueAccessor {
  readonly id = `soft-input-${++uid}`;
  readonly label = input('');
  readonly type = input<'text' | 'email' | 'password' | 'number' | 'date' | 'time'>('text');
  readonly placeholder = input('');
  readonly required = input(false);
  readonly hint = input('');
  readonly error = input('');

  readonly value = signal<string>('');
  readonly disabled = signal(false);

  private onChange: (v: unknown) => void = () => {};
  onTouched: () => void = () => {};

  onInput(event: Event): void {
    const raw = (event.target as HTMLInputElement).value;
    this.value.set(raw);
    this.onChange(this.type() === 'number' ? (raw === '' ? null : Number(raw)) : raw);
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
