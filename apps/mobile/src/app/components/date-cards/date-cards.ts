import { Component, inject, input, output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { SharingService, OutingUpdate } from '../nav-bar/sharing/sharing.service';

@Component({
  selector:'app-date-cards',imports:[DatePipe,FormsModule],templateUrl:'./date-cards.html',styleUrl:'./date-cards.scss'
})
export class DateCardsComponent {
  readonly sharing=inject(SharingService);
  readonly notifications=input(false);
  readonly pickYours=output<OutingUpdate>(); readonly openPicks=output<void>();
  cancelling:string|null=null;
  reason='';
  startCancellation(update:OutingUpdate):void {this.cancelling=update.revision;this.reason='';}
  async cancel(update:OutingUpdate):Promise<void> {if(await this.sharing.cancel(update.revision,this.reason))this.cancelling=null;}
  canReply(update:OutingUpdate):boolean {return !update.cancelled && !update.mine && !!update.venue?.activity && !!update.venue.startUtc && Date.parse(update.venue.startUtc)+3600000>Date.now();}
  groups(): {label:string;updates:OutingUpdate[]}[] {
    const all=this.sharing.state()?.outingUpdates || [];
    const updates=all.filter(x=>!x.cancelled);
    if(this.notifications()) return [{label:'',updates:all}];
    const now=Date.now();
    const time=(update:OutingUpdate)=>update.venue?.startUtc ? Date.parse(update.venue.startUtc) : NaN;
    return [
      {label:'Upcoming dates',updates:updates.filter(x=>time(x)>=now).sort((a,b)=>time(a)-time(b))},
      {label:'Shared places & updates',updates:updates.filter(x=>!Number.isFinite(time(x)))},
      {label:'Cancelled dates',updates:all.filter(x=>x.cancelled)},
      {label:'Past dates',updates:updates.filter(x=>time(x)<now).sort((a,b)=>time(b)-time(a))}
    ];
  }
}
