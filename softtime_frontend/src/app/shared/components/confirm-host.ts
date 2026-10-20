import { ChangeDetectionStrategy, Component, ElementRef, HostListener, inject, ViewChild } from '@angular/core';
import { ConfirmService } from '../../core/confirm.service';
import { SoftButton } from './soft-button';

@Component({
  selector: 'confirm-host',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [SoftButton],
  template: `
    @if (confirm.state(); as s) {
      <div class="soft-modal__overlay" (click)="onOverlay($event)">
        <div
          #panel
          class="soft-modal__panel soft-modal__panel--sm"
          role="dialog"
          aria-modal="true"
          aria-labelledby="confirm-dialog-title"
          tabindex="-1"
          (keydown)="onKeydown($event)"
        >
          <div class="soft-modal__header">
            <h3 id="confirm-dialog-title">{{ s.title }}</h3>
            <button class="soft-modal__close" type="button" aria-label="Fermer la confirmation" (click)="confirm.answer(false)">
              &times;
            </button>
          </div>
          <div class="soft-modal__body">
            <p>{{ s.message }}</p>
          </div>
          <div class="soft-modal__footer">
            <soft-button variant="ghost" (click)="confirm.answer(false)">Annuler</soft-button>
            <soft-button variant="danger" (click)="confirm.answer(true)">{{ s.okText }}</soft-button>
          </div>
        </div>
      </div>
    }
  `,
})
export class ConfirmHost {
  readonly confirm = inject(ConfirmService);
  private previousFocus: HTMLElement | null = null;

  @ViewChild('panel')
  set panel(element: ElementRef<HTMLElement> | undefined) {
    if (!element) {
      this.previousFocus?.focus();
      this.previousFocus = null;
      return;
    }

    this.previousFocus = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    queueMicrotask(() => this.focusFirst(element.nativeElement));
  }

  onOverlay(event: MouseEvent): void {
    if ((event.target as HTMLElement).classList.contains('soft-modal__overlay')) {
      this.confirm.answer(false);
    }
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Tab') return;
    const panel = event.currentTarget as HTMLElement;
    const focusable = this.focusableElements(panel);
    if (!focusable.length) {
      event.preventDefault();
      panel.focus();
      return;
    }
    const first = focusable[0];
    const last = focusable[focusable.length - 1];
    if (event.shiftKey && document.activeElement === first) {
      event.preventDefault();
      last.focus();
    } else if (!event.shiftKey && document.activeElement === last) {
      event.preventDefault();
      first.focus();
    }
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.confirm.state()) {
      this.confirm.answer(false);
    }
  }

  private focusFirst(panel: HTMLElement): void {
    const first = this.focusableElements(panel)[0];
    (first ?? panel).focus();
  }

  private focusableElements(panel: HTMLElement): HTMLElement[] {
    return Array.from(panel.querySelectorAll<HTMLElement>(
      'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])',
    ));
  }
}
