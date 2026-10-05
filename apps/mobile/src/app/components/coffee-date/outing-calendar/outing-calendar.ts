import { Component, input, output } from '@angular/core';

@Component({
  selector:'app-outing-calendar',
  template:`
    <section aria-label="Choose a date">
      <header><button type="button" aria-label="Previous month" [disabled]="previousDisabled()" (click)="move(-1)"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m14 6-6 6 6 6"/></svg></button><strong aria-live="polite">{{monthLabel()}}</strong><button type="button" aria-label="Next month" (click)="move(1)"><svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m10 6 6 6-6 6"/></svg></button></header>
      <div class="week" aria-hidden="true">@for(day of ['M','T','W','T','F','S','S'];track $index){<span>{{day}}</span>}</div>
      <div class="days">
        @for(day of days();track $index) {
          @if(day) { <button type="button" [disabled]="key(day)<minimum()" [attr.aria-label]="key(day)" [attr.aria-pressed]="value()===key(day)" (click)="select(day)">{{day}}</button> }
          @else { <span></span> }
        }
      </div>
    </section>`,
  styles:[`
    :host{display:block}section{padding:.75rem;border:1px solid var(--line);border-radius:1.25rem;background:var(--surface)}header{display:flex;align-items:center;justify-content:space-between;margin-bottom:.75rem}strong{font-size:.95rem}button{border:0;background:transparent;color:var(--text);font:inherit;cursor:pointer;min-height:44px;border-radius:.8rem}header button{display:grid;place-items:center;width:44px;font-size:1.5rem}.week,.days{display:grid;grid-template-columns:repeat(7,minmax(0,1fr));gap:2px;text-align:center}.week{font-size:.7rem;color:var(--muted);margin-bottom:.5rem}.days button[aria-pressed=true]{background:var(--accent);color:var(--on-accent);font-weight:600}button:disabled{opacity:.3;cursor:default}button:hover:not(:disabled){background:var(--accent-soft)}button:focus-visible{outline:2px solid var(--accent);outline-offset:-2px}
  `]
})
export class OutingCalendarComponent {
  minimum():string {const now=new Date();return now.getFullYear()+'-'+String(now.getMonth()+1).padStart(2,'0')+'-'+String(now.getDate()).padStart(2,'0');}
  previousDisabled():boolean {return this.key(1)<=this.minimum().slice(0,7)+'-01';}
  select(day:number):void {if(this.key(day)>=this.minimum())this.valueChange.emit(this.key(day));}

  readonly value=input('');
  readonly valueChange=output<string>();
  private readonly today=new Date();
  year=this.today.getFullYear(); month=this.today.getMonth();
  monthLabel(): string { return new Intl.DateTimeFormat('en',{month:'long',year:'numeric',timeZone:'UTC'}).format(new Date(Date.UTC(this.year,this.month,1))); }
  move(delta:number):void { if(delta<0 && this.previousDisabled())return; const next=new Date(Date.UTC(this.year,this.month+delta,1));this.year=next.getUTCFullYear();this.month=next.getUTCMonth(); }
  days():number[] { const offset=(new Date(Date.UTC(this.year,this.month,1)).getUTCDay()+6)%7;const count=new Date(Date.UTC(this.year,this.month+1,0)).getUTCDate();return [...Array(offset).fill(0),...Array.from({length:count},(_,i)=>i+1)]; }
  key(day:number):string { return this.year+'-'+String(this.month+1).padStart(2,'0')+'-'+String(day).padStart(2,'0'); }
}
