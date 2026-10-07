import { LoadingIndicatorComponent } from '../loading-indicator/loading-indicator';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../auth.service';

@Component({selector:'app-profile-timezone',imports:[FormsModule,LoadingIndicatorComponent],template:`
  @if(busy()){<app-loading-indicator [inline]="true" label="Updating your timezone..." />}
  <form (ngSubmit)="save()">
    <label for="profile-timezone">Your timezone</label>
    <p>Travelling? Update your timezone for your schedule and daily reminder.</p>
    <select id="profile-timezone" name="timezone" [(ngModel)]="zone" [disabled]="busy() || !ready()">@for(item of zones();track item){<option [value]="item">{{item.replaceAll('_',' ')}}</option>}</select>
    <button type="submit" [disabled]="busy() || !ready()">Save timezone</button>
    @if(error()){<p role="alert">{{error()}}</p>}@if(notice()){<p role="status">{{notice()}}</p>}
  </form>`,styles:`
  :host{display:block;margin-top:1.25rem}form{padding:1rem;border:1px solid var(--line);border-radius:1rem;background:var(--surface)}label{font-weight:650}p{font-size:.85rem;line-height:1.6;color:var(--muted)}select,button{font:inherit;color:var(--text);min-height:48px;border:1px solid var(--line);border-radius:.8rem;padding:.7rem;background:var(--field)}select{width:100%;box-sizing:border-box}button{margin-top:.75rem;background:var(--surface);cursor:pointer}button:disabled,select:disabled{opacity:.5}:is(button,select):focus-visible{outline:2px solid var(--accent);outline-offset:3px}
`})
export class ProfileTimezoneComponent implements OnInit {
  private readonly http=inject(HttpClient);private readonly auth=inject(AuthService);
  readonly zones=signal<string[]>([]);readonly busy=signal(false);readonly ready=signal(false);readonly error=signal('');readonly notice=signal('');
  zone=Intl.DateTimeFormat().resolvedOptions().timeZone;
  async ngOnInit():Promise<void>{this.busy.set(true);try{const result=await firstValueFrom(this.http.get<{timeZone:string|null;zones:string[]}>('/api/auth/calendar/preferences'));this.zone=result.timeZone || this.zone;this.zones.set([...new Set([this.zone,...result.zones])]);this.ready.set(true);}catch{this.error.set('Could not load your timezone. Reopen Profile to retry.');}finally{this.busy.set(false);}}
  async save():Promise<void>{if(this.busy() || !this.ready())return;this.busy.set(true);this.error.set('');this.notice.set('');try{await this.auth.submit('calendar/timezone',{timeZone:this.zone});this.notice.set('Timezone saved. Your daily reminder follows this timezone.');}catch{this.error.set('Could not save your timezone. Try again.');}finally{this.busy.set(false);}}
}
