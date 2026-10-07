import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-outing-choice',
  template: `
    <details #menu (keydown.escape)="menu.open=false; trigger.focus()">
      <summary #trigger><span><small>{{label()}}</small><strong>{{selectedLabel()}}</strong></span><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m6 9 6 6 6-6"/></svg></summary>
      <div class="options" role="group" [attr.aria-label]="label()">
        @for(option of options(); track option.value) {
          <button type="button" [attr.aria-pressed]="value()===option.value"
            (click)="valueChange.emit(option.value);menu.open=false;trigger.focus()">
            {{option.label}} <span aria-hidden="true">@if(value()===option.value){<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m5 12 4 4L19 6"/></svg>}</span>
          </button>
        }
      </div>
    </details>`,
  styles: [`
    :host{display:block;min-width:0}details{position:relative}summary{list-style:none;display:flex;align-items:center;justify-content:space-between;gap:1rem;padding:1rem;border:1px solid var(--line);border-radius:1rem;background:var(--surface);cursor:pointer;min-height:72px}summary::-webkit-details-marker{display:none}small{display:block;color:var(--muted);font-size:.75rem;margin-bottom:.4rem}strong{font-size:1rem;font-weight:500}summary:focus-visible,button:focus-visible{outline:2px solid var(--accent);outline-offset:2px}.options{position:relative;background:var(--surface);border:1px solid var(--line);border-radius:1rem;margin-top:.4rem;padding:.35rem;max-height:240px;overflow:auto}button{display:flex;justify-content:space-between;align-items:center;width:100%;min-height:44px;border:0;border-radius:.65rem;padding:.6rem .8rem;text-align:left;font:inherit;color:var(--text);background:transparent;cursor:pointer}button[aria-pressed=true]{background:var(--accent-soft);color:var(--text)}button span{color:var(--accent)}
    :host([compact]) summary{min-height:44px;padding:.5rem .65rem;border-radius:.7rem;gap:.5rem}:host([compact]) summary>span{display:flex;align-items:center;gap:.6rem;min-width:0}:host([compact]) small{margin:0;font-size:.75rem}:host([compact]) strong{font-size:.85rem}:host([compact]) summary>svg{width:14px;flex-shrink:0}
  `]
})
export class OutingChoiceComponent {
  readonly label=input.required<string>();
  readonly value=input.required<string>();
  readonly options=input.required<{value:string;label:string}[]>();
  readonly valueChange=output<string>();
  selectedLabel(): string { return this.options().find(x=>x.value===this.value())?.label || 'Choose'; }
}
