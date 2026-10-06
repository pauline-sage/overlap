import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { KeyboardDismissalDirective } from './keyboard-dismissal.directive';

@Component({
  imports: [KeyboardDismissalDirective],
  template: `<section appKeyboardDismissal>
    <form (submit)="$event.preventDefault(); submitted = true">
      <input id="search" type="search" enterkeyhint="search" />
      <input id="next" enterkeyhint="next" />
      <textarea></textarea>
      <button type="submit"><span>Save</span></button>
    </form>
  </section>`,
})
class KeyboardTestHost { submitted = false; }

describe('App keyboard dismissal', () => {
  it('dismisses completed single-line input without blocking its Enter event', () => {
    const f = TestBed.createComponent(KeyboardTestHost); f.detectChanges();
    const field = f.nativeElement.querySelector('#search') as HTMLInputElement;
    field.focus(); const event = new KeyboardEvent('keydown', {key:'Enter', bubbles:true, cancelable:true});
    field.dispatchEvent(event);
    expect(document.activeElement).not.toBe(field); expect(event.defaultPrevented).toBe(false); f.destroy();
  });
  it('keeps the keyboard for next-field input, multiline entry and composition', () => {
    const f = TestBed.createComponent(KeyboardTestHost); f.detectChanges();
    for (const selector of ['#next', 'textarea']) {
      const field = f.nativeElement.querySelector(selector) as HTMLElement; field.focus();
      field.dispatchEvent(new KeyboardEvent('keydown', {key:'Enter', bubbles:true}));
      expect(document.activeElement).toBe(field);
    }
    const search = f.nativeElement.querySelector('#search') as HTMLInputElement; search.focus();
    search.dispatchEvent(new KeyboardEvent('keydown', {key:'Enter', bubbles:true, isComposing:true}));
    expect(document.activeElement).toBe(search); f.destroy();
  });
  it('dismisses input on a touch action or submission and preserves the form action', () => {
    const f = TestBed.createComponent(KeyboardTestHost); f.detectChanges();
    const field = f.nativeElement.querySelector('textarea') as HTMLTextAreaElement; field.focus();
    f.nativeElement.querySelector('button span').dispatchEvent(new Event('pointerdown', {bubbles:true}));
    expect(document.activeElement).not.toBe(field);
    field.focus(); f.nativeElement.querySelector('form').dispatchEvent(new Event('submit', {bubbles:true, cancelable:true}));
    expect(document.activeElement).not.toBe(field); expect(f.componentInstance.submitted).toBe(true); f.destroy();
  });
});
