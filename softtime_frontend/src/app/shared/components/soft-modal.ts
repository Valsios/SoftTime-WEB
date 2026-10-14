import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  input,
  output,
  ViewChild,
} from '@angular/core';

let modalSequence = 0;

@Component({
  selector: 'soft-modal',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (open()) {
      <div class="soft-modal__overlay" (click)="onOverlay($event)">
        <div
          #panel
          class="soft-modal__panel"
          [class]="'soft-modal__panel--' + size()"
          role="dialog"
          aria-modal="true"
          [attr.aria-labelledby]="titleId"
          tabindex="-1"
          (keydown)="onKeydown($event)"
        >
          <div class="soft-modal__header">
            <h3 [id]="titleId">{{ title() }}</h3>
            <button class="soft-modal__close" type="button" [attr.aria-label]="'Fermer la fenêtre ' + title()" (click)="closed.emit()">
              &times;
            </button>
          </div>
          <div class="soft-modal__body">
            <ng-content></ng-content>
          </div>
          <div class="soft-modal__footer">
            <ng-content select="[footer]"></ng-content>
          </div>
        </div>
      </div>
    }
  `,
})
export class SoftModal {
  readonly open = input(false);
  readonly title = input('');
  readonly size = input<'sm' | 'md' | 'lg' | 'xl'>('md');
  readonly closed = output<void>();
  readonly titleId = `soft-modal-title-${modalSequence++}`;
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
      this.closed.emit();
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
    if (this.open()) this.closed.emit();
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
