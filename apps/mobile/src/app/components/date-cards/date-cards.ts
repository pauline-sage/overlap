import { LoadingIndicatorComponent } from '../loading-indicator';
import { Component, inject, input, output, signal, DestroyRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { SharingService, OutingUpdate } from '../nav-bar/sharing/sharing.service';

@Component({
  selector:'app-date-cards',imports: [LoadingIndicatorComponent,DatePipe,FormsModule],templateUrl:'./date-cards.html',styleUrl:'./date-cards.scss'
})
export class DateCardsComponent {
  private readonly now=signal(Date.now());
  readonly expanded=signal<string|null>(null);
  constructor(){
    const refresh=()=>this.now.set(Date.now());
    const timer=setInterval(refresh,15000);
    document.addEventListener('visibilitychange',refresh);
    inject(DestroyRef).onDestroy(()=>{clearInterval(timer);document.removeEventListener('visibilitychange',refresh);});
  }
  togglePast(revision:string):void {this.expanded.update(x=>x===revision?null:revision);}
  readonly sharing=inject(SharingService);
  readonly notifications=input(false);
  readonly pickYours=output<OutingUpdate>(); readonly openPicks=output<void>();
  readonly deviceZone=Intl.DateTimeFormat().resolvedOptions().timeZone;
  localTime(instant:string, zone:string):string {
    try {return new Intl.DateTimeFormat('en-GB',{timeZone:zone,weekday:'short',day:'numeric',month:'short',hour:'2-digit',minute:'2-digit',hourCycle:'h23'}).format(new Date(instant));}
    catch {return 'Time unavailable';}
  }
  cancelling:string|null=null;
  reason='';
  startCancellation(update:OutingUpdate):void {this.cancelling=update.revision;this.reason='';}
  async cancel(update:OutingUpdate):Promise<void> {if(await this.sharing.cancel(update.revision,this.reason))this.cancelling=null;}
  canReply(update:OutingUpdate):boolean {return !update.cancelled && !update.mine && !!update.venue?.activity && !!update.venue.startUtc && Date.parse(update.venue.startUtc)+3600000>Date.now();}
  visibleMovieNotifications():boolean {return !!this.sharing.state()?.moviePicks?.some(x=>x.shared && !x.dismissed);}
  groups(): {label:string;updates:OutingUpdate[]}[] {
    const all=this.sharing.state()?.outingUpdates || [];
    const updates=all.filter(x=>!x.cancelled);
    if(this.notifications()) return [{label:'',updates:all.filter(x=>!x.dismissed)}];
    const now=this.now();
    const time=(update:OutingUpdate)=>update.venue?.startUtc ? Date.parse(update.venue.startUtc) : NaN;
    return [
      {label:'Upcoming dates',updates:updates.filter(x=>time(x)>=now).sort((a,b)=>time(a)-time(b))},
      {label:'Shared places & updates',updates:updates.filter(x=>!Number.isFinite(time(x)))},
      {label:'Past dates',updates:updates.filter(x=>time(x)<now).sort((a,b)=>time(b)-time(a))}
    ];
  }
}
