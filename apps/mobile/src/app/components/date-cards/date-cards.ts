import { LoadingIndicatorComponent } from '../loading-indicator/loading-indicator';
import { Component, inject, input, output, signal, DestroyRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { SharingService, OutingUpdate, CookingContest } from '../nav-bar/sharing/sharing.service';

@Component({
  selector:'app-date-cards',imports: [LoadingIndicatorComponent,DatePipe,FormsModule],templateUrl:'./date-cards.html',styleUrl:'./date-cards.scss'
})
export class DateCardsComponent {
  private readonly now=signal(Date.now());
  readonly dateTab=signal<'upcoming'|'passed'>('upcoming');
  readonly expanded=signal<string|null>(null);
  dateTabKey(event:KeyboardEvent):void {
    if(!['ArrowLeft','ArrowRight','Home','End'].includes(event.key))return;event.preventDefault();
    this.dateTab.set(event.key==='Home' ? 'upcoming' : event.key==='End' ? 'passed' : this.dateTab()==='upcoming' ? 'passed' : 'upcoming');
    document.getElementById('dates-tab-'+this.dateTab())?.focus();
  }
  displayedGroups():ReturnType<DateCardsComponent['groups']>{const groups=this.groups();return this.notifications() || this.contestsOnly() ? groups : groups.filter(x=>x.label===(this.dateTab()==='upcoming' ? 'Upcoming dates' : 'Past dates'));}
  constructor(){
    const refresh=()=>this.now.set(Date.now());
    const timer=setInterval(refresh,15000);
    document.addEventListener('visibilitychange',refresh);
    inject(DestroyRef).onDestroy(()=>{clearInterval(timer);document.removeEventListener('visibilitychange',refresh);});
  }
  togglePast(revision:string):void {this.expanded.update(x=>x===revision?null:revision);}
  readonly sharing=inject(SharingService);
  readonly notifications=input(false);
  readonly contestsOnly=input(false);
  readonly pickYours=output<OutingUpdate>(); readonly openCooking=output<number>(); readonly openContest=output<number>(); readonly openPicks=output<void>();
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
  contestFuture(contest:CookingContest):boolean {return Date.parse(contest.startUtc)>this.now();}
  async cancelContest(contest:CookingContest):Promise<void> {if(await this.sharing.contestAction(contest,'cancel'))this.cancelling=null;}
  visibleRecipeNotifications():boolean {return !!this.sharing.state()?.recipeShares?.some(x=>!x.dismissed);}
  visibleMovieNotifications():boolean {return !!this.sharing.state()?.moviePicks?.some(x=>x.shared && !x.dismissed);}
  groups(): {label:string;updates:OutingUpdate[];contests:CookingContest[]}[] {
    const contests=this.sharing.state()?.cookingContests || [];
    if(this.contestsOnly())return [{label:'Your contests',updates:[],contests:[...contests].reverse()}];
    const all=this.sharing.state()?.outingUpdates || [];
    const updates=all.filter(x=>!x.cancelled);
    if(this.notifications()) return [{label:'',updates:all.filter(x=>!x.dismissed),contests:contests.filter(x=>!x.dismissed)}];
    const now=this.now();
    const accepted=contests.filter(x=>x.status==='accepted');
    const time=(update:OutingUpdate)=>update.venue?.startUtc ? Date.parse(update.venue.startUtc) : NaN;
    return [
      {label:'Upcoming dates',updates:updates.filter(x=>time(x)>=now).sort((a,b)=>time(a)-time(b)),contests:accepted.filter(x=>Date.parse(x.startUtc)>=now)},
      {label:'Shared places & updates',updates:updates.filter(x=>!Number.isFinite(time(x))),contests:[]},
      {label:'Past dates',updates:updates.filter(x=>time(x)<now).sort((a,b)=>time(b)-time(a)),contests:accepted.filter(x=>Date.parse(x.startUtc)<now).reverse()}
    ];
  }
}
