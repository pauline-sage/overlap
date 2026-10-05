import {Component,inject,signal,OnInit} from '@angular/core';
import {HttpClient,HttpErrorResponse} from '@angular/common/http';
import {firstValueFrom} from 'rxjs';
import {AuthService} from '../../../../auth.service';

@Component({selector:'app-phone-reminders',template:`
  <section class="reminder-card" aria-labelledby="phone-reminder-title">
    <div class="copy"><strong id="phone-reminder-title">Cooking reminders</strong><p>A phone notification a minute or two before your accepted contest. Enabled on this device only.</p></div>
    <button type="button" role="switch" aria-label="Cooking reminders on this device" [attr.aria-checked]="enabled()" [disabled]="busy() || !supported || !available()" (click)="toggle()"><span class="track" [class.on]="enabled()" aria-hidden="true"></span><small>{{enabled()?'On':'Off'}}</small></button>
    @if(!supported){<p class="help">On iPhone, add Overlap to your Home Screen and open it there to enable notifications. Otherwise, use a browser that supports phone notifications.</p>}
    @else if(!available() && !busy()){<p class="help">Phone reminders are not configured yet.</p>}
    @if(supported){<p class="help">Only notification permission is needed. Reminders can arrive while Overlap is closed; delivery may be delayed if your phone is offline.</p>}
    @if(error()){<p class="help" role="alert">{{error()}}</p>}
  </section>`,styles:`
  :host{display:block}.reminder-card{display:flex;align-items:center;flex-wrap:wrap;gap:1rem;padding:1rem;border:1px solid var(--line);border-radius:1rem;background:var(--surface)}.copy{flex:1;min-width:180px}.copy strong{font-size:1rem;font-weight:650}.copy p,.help{font-size:.85rem;line-height:1.6;color:var(--muted);margin:.4rem 0 0}.help{flex-basis:100%}button{display:grid;justify-items:center;gap:.25rem;min-width:52px;min-height:44px;border:0;background:transparent;color:var(--muted);cursor:pointer;font:inherit}small{font-size:.7rem}.track{width:44px;height:26px;box-sizing:border-box;border:1px solid var(--muted);padding:3px;border-radius:2rem}.track::after{content:'';display:block;width:18px;height:18px;background:var(--muted);border-radius:50%}.track.on{background:var(--accent);border-color:var(--accent)}.track.on::after{background:#172511;transform:translateX(18px)}button:disabled{opacity:.5;cursor:default}button:focus-visible{outline:2px solid var(--accent);outline-offset:3px}
  `})
export class PhoneRemindersComponent implements OnInit {
  private readonly auth=inject(AuthService);private readonly http=inject(HttpClient);
  readonly supported=window.isSecureContext && 'serviceWorker' in navigator && 'PushManager' in window && 'Notification' in window;
  readonly busy=signal(false);readonly available=signal(false);readonly enabled=signal(false);readonly error=signal('');
  private publicKey='';
  async ngOnInit():Promise<void>{
    if(!this.supported)return;this.busy.set(true);
    try{const config=await firstValueFrom(this.http.get<{available:boolean;publicKey:string|null}>('/api/auth/phone-reminders'));this.available.set(config.available);this.publicKey=config.publicKey || '';
      const registration=await navigator.serviceWorker.getRegistration('/');const subscription=await registration?.pushManager.getSubscription();
      if(subscription){const status=await this.auth.submit<{enabled:boolean}>('phone-reminders/status',{endpoint:subscription.endpoint});this.enabled.set(status.enabled && Notification.permission==='granted');}
    }catch{this.error.set('Could not load reminder settings. Reopen Settings to retry.');}finally{this.busy.set(false);}
  }
  async toggle():Promise<void>{
    if(this.busy() || !this.available())return;this.busy.set(true);this.error.set('');
    try{
      if(this.enabled()){
        const registration=await navigator.serviceWorker.getRegistration('/');const subscription=await registration?.pushManager.getSubscription();
        if(subscription){await this.auth.submit('phone-reminders/unsubscribe',{endpoint:subscription.endpoint});await subscription.unsubscribe();}this.enabled.set(false);
      }else{
        // Ask directly from the tap; iOS requires a user gesture.
        if(await Notification.requestPermission()!=='granted'){this.error.set('Allow notifications in your phone or browser settings to receive reminders.');return;}
        await navigator.serviceWorker.register('/phone-reminders-sw.js',{scope:'/'});const registration=await navigator.serviceWorker.ready;
        const key=this.publicKey.replace(/-/g,'+').replace(/_/g,'/');const bytes=Uint8Array.from(atob(key.padEnd(Math.ceil(key.length/4)*4,'=')),x=>x.charCodeAt(0));
        const subscription=await registration.pushManager.getSubscription() || await registration.pushManager.subscribe({userVisibleOnly:true,applicationServerKey:bytes});
        try{await this.auth.submit('phone-reminders/subscribe',subscription.toJSON());}catch(e){await subscription.unsubscribe();throw e;}
        this.enabled.set(true);
      }
    }catch(e){this.error.set(e instanceof HttpErrorResponse ? e.error?.title || 'Could not update phone reminders. Try again.' : 'Could not update phone reminders. Try again.');}finally{this.busy.set(false);}
  }
}
