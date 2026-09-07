import { ChangeDetectionStrategy, Component, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

@Component({
  selector: 'soft-toggle',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => SoftToggle), multi: true },
  ],
  template: `
    <label class="soft-toggle">
      <input
        type="checkbox"
        [checked]="checked()"
        [disabled]="disabled()"
        (change)="onToggle($event)"
        (blur)="onTouched()"
      />
      <span class="soft-toggle__track">
        <span class="soft-toggle__thumb"></span>
      </span>
    </label>
  `,
})
export class SoftToggle implements ControlValueAccessor {
  readonly checked = signal(false);
  readonly disabled = signal(false);

  private onChange: (v: boolean) => void = () => {};
  onTouched: () => void = () => {};

  onToggle(event: Event): void {
    const value = (event.target as HTMLInputElement).checked;
    this.checked.set(value);
    this.onChange(value);
  }

  writeValue(v: boolean): void {
    this.checked.set(!!v);
  }
  registerOnChange(fn: (v: boolean) => void): void {
    this.onChange = fn;
  }
  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }
  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }
}
