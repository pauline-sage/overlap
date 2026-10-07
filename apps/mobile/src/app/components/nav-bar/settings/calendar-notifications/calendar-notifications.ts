import { LoadingIndicatorComponent } from '../../../loading-indicator/loading-indicator';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../../../auth.service';

@Component({selector:'app-calendar-notifications',imports:[FormsModule,LoadingIndicatorComponent],template:`
  @if(busy()){<app-loading-indicator [inline]="true" label="Updating notification settings..." />}
  <section class="reminder-card">
    <div class="toggle-row"><div><strong>Daily schedule notification</strong><p>One reminder showing today's number of events, including birthdays. No names or titles.</p></div><button type="button" role="switch" aria-label="Daily schedule notification" [attr.aria-checked]="enabled()" [disabled]="busy() || !ready()" (click)="toggle()"><span class="track" [class.on]="enabled()" aria-hidden="true"></span><small>{{enabled() ? 'On' : 'Off'}}</small></button></div>
    @if(enabled()) {
      <form (ngSubmit)="save()"><div class="fields"><div><label for="calendar-notify-time">Daily time</label><input id="calendar-notify-time" name="time" type="time" [(ngModel)]="time" required /></div><div><label for="calendar-notify-zone">Your timezone</label><select id="calendar-notify-zone" name="zone" [(ngModel)]="zone">@for(item of zones();track item){<option [value]="item">{{item}}</option>}</select></div></div><button class="save-time" type="submit" [disabled]="busy()">Save notification time</button></form>
      <p>Turn on notifications for this device above to receive your daily reminder.</p>
    }
    @if(error()){<p role="alert">{{error()}}</p>}@if(notice()){<p role="status">{{notice()}}</p>}
  </section>`,styles:`
  :host{display:block;border-top:1px solid var(--line)}.reminder-card{padding:1rem;border:0;border-radius:0;background:transparent}.toggle-row{display:flex;align-items:center;gap:1rem}.toggle-row>div{flex:1;min-width:0}p{font-size:.8rem;color:var(--muted);line-height:1.6;margin:.4rem 0}button,input,select{font:inherit;min-height:44px;color:var(--text)}button{cursor:pointer}.toggle-row button{display:grid;justify-items:center;gap:.25rem;min-width:52px;border:0;background:transparent}small{font-size:.7rem}.track{width:44px;height:26px;box-sizing:border-box;border:1px solid var(--muted);padding:3px;border-radius:2rem}.track::after{content:'';display:block;width:18px;height:18px;background:var(--muted);border-radius:50%}.track.on{background:var(--accent);border-color:var(--accent)}.track.on::after{background:var(--on-accent);transform:translateX(18px)}.fields{display:grid;grid-template-columns:minmax(0,1fr) minmax(0,1.5fr);gap:.75rem;margin-top:1rem}label{display:block;font-size:.8rem;margin-bottom:.35rem}input,select{box-sizing:border-box;width:100%;padding:.6rem;background:var(--field);border:1px solid var(--line);border-radius:.75rem}.save-time{padding:.5rem .8rem;margin:.75rem 0;border:1px solid var(--line);border-radius:.75rem;background:var(--surface)}button:disabled{opacity:.5}:is(button,input,select):focus-visible{outline:2px solid var(--accent);outline-offset:3px}
`})
export class CalendarNotificationsComponent implements OnInit {
  private readonly http=inject(HttpClient);private readonly auth=inject(AuthService);
  readonly enabled=signal(false);readonly busy=signal(false);readonly ready=signal(false);readonly error=signal('');readonly notice=signal('');readonly zones=signal<string[]>([]);
  time='09:00';zone=Intl.DateTimeFormat().resolvedOptions().timeZone;
  async ngOnInit():Promise<void>{this.busy.set(true);try{const result=await firstValueFrom(this.http.get<{dailyEnabled:boolean;timeZone:string|null;notifyMinutes:number;zones:string[]}>('/api/auth/calendar/preferences'));this.enabled.set(result.dailyEnabled);this.zone=result.timeZone || this.zone;this.zones.set([...new Set([this.zone,...result.zones])]);this.time=String(Math.floor(result.notifyMinutes/60)).padStart(2,'0')+':'+String(result.notifyMinutes%60).padStart(2,'0');this.ready.set(true);}catch{this.error.set('Could not load calendar notifications. Reopen Settings to retry.');}finally{this.busy.set(false);}}
  async toggle():Promise<void>{if(this.busy() || !this.ready())return;await this.save(!this.enabled());}
  async save(enabled=this.enabled()):Promise<void>{if(this.busy())return;this.busy.set(true);this.error.set('');this.notice.set('');try{const parts=this.time.split(':').map(Number);await this.auth.submit('calendar/preferences',{dailyEnabled:enabled,timeZone:this.zone,notifyMinutes:parts[0]*60+parts[1]});this.enabled.set(enabled);this.notice.set(enabled ? 'Daily schedule notifications enabled. Phone notifications must also be on for this device.' : 'Daily schedule notifications disabled.');}catch(error){this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Could not save this setting.' : 'Could not save this setting.');}finally{this.busy.set(false);}}
}
