import { Directive, ElementRef, HostListener, inject } from '@angular/core';

/** Dismiss the phone keyboard on completed input and explicit actions. */
@Directive({ selector: '[appKeyboardDismissal]' })
export class KeyboardDismissalDirective {
  private readonly host = inject(ElementRef<HTMLElement>);

  @HostListener('keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    const field = event.target;
    if (event.key !== 'Enter' || event.isComposing || !(field instanceof HTMLInputElement)) return;
    const hint = field.getAttribute('enterkeyhint');
    if (hint === 'next' || hint === 'previous') return;
    // Keep default submission and feature-specific Enter handlers intact.
    this.dismiss();
  }

  @HostListener('submit')
  onSubmit(): void { this.dismiss(); }

  @HostListener('pointerdown', ['$event'])
  onAction(event: PointerEvent): void {
    if (!(event.target instanceof Element)) return;
    const action = event.target.closest('button, a[href], [role="button"]');
    if (!action || action.matches(':disabled, [aria-disabled="true"]')) return;
    // Blur before the action takes focus, including on mobile browsers.
    this.dismiss();
  }

  private dismiss(): void {
    const field = document.activeElement;
    if (this.host.nativeElement.contains(field) &&
        (field instanceof HTMLInputElement || field instanceof HTMLTextAreaElement)) field.blur();
  }
}
