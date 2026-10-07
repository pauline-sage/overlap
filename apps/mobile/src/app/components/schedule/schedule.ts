import { Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { OutingCalendarComponent } from '../coffee-date/outing-calendar/outing-calendar';
import { OutingChoiceComponent } from '../coffee-date/outing-choice/outing-choice';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../auth.service';
import { CollectionBrowser, CollectionBrowserComponent } from '../collection-browser/collection-browser';
import { LoadingIndicatorComponent } from '../loading-indicator/loading-indicator';
import { OutingUpdate, CookingContest } from '../nav-bar/sharing/sharing.service';

interface CalendarRow {
  id:number;title:string;note:string|null;mine:boolean;allDay:boolean;yearly:boolean;timeZone:string;
  date:string;endDate:string;startUtc:string|null;endUtc:string|null;
  sourceDate:string;sourceEndDate:string;startTime:string;endTime:string;revision:string;
}
interface Appointment {id:string;title:string;start?:string;end?:string;mine:boolean;sharedDate?:boolean;note?:string;entry?:CalendarRow;}
interface CalendarList {connected:boolean;items:CalendarRow[];ownToday:number;partnerToday:number;}

@Component({
  selector:'app-schedule',imports:[OutingCalendarComponent,OutingChoiceComponent,DatePipe,FormsModule,CollectionBrowserComponent,LoadingIndicatorComponent],
  templateUrl:'./schedule.html',styleUrl:'./schedule.scss',
})
export class ScheduleComponent {
  readonly opened=input(false);readonly active=input(true);readonly connectionId=input('');
  readonly partnerName=input('Your person');readonly ownName=input('You');
  readonly outings=input<OutingUpdate[]>([]);readonly contests=input<CookingContest[]>([]);
  readonly open=output<void>();readonly back=output<void>();
  private readonly http=inject(HttpClient);private readonly auth=inject(AuthService);
  zone=Intl.DateTimeFormat().resolvedOptions().timeZone;
  private readonly today=new Date();
  readonly selected=signal(this.dayKey(this.today));readonly month=signal(new Date(this.today.getFullYear(),this.today.getMonth(),1));
  readonly monthOpened=signal(false);readonly formOpened=signal(false);readonly editing=signal<CalendarRow|null>(null);
  readonly removing=signal<string|null>(null);readonly error=signal('');readonly notice=signal('');
  readonly busy=signal(false);readonly loading=signal(false);readonly connected=signal(false);readonly ready=signal(false);
  readonly entries=signal<CalendarRow[]>([]);readonly todayCounts=signal({own:0,partner:0});readonly zones=signal([this.zone]);
  readonly pager=new CollectionBrowser();
  readonly hours=Array.from({length:24},(_,i)=>({value:String(i).padStart(2,'0'),label:String(i).padStart(2,'0')}));
  readonly minutes=Array.from({length:60},(_,i)=>({value:String(i).padStart(2,'0'),label:String(i).padStart(2,'0')}));
  setTime(which:'start'|'end',part:'hour'|'minute',value:string):void { const current=(which==='start'?this.startTime:this.endTime).split(':'); current[part==='hour'?0:1]=value; if(which==='start')this.startTime=current.join(':');else this.endTime=current.join(':'); }
  title='';startTime='09:00';endTime='10:00';note='';entryDate='';endDate='';timeZone=this.zone;allDay=false;yearly=false;
  private version=0;private disposed=false;
  constructor(){
    effect(()=>{this.active();this.month();this.connectionId();untracked(()=>void this.load());});
    const refresh=()=>{if(document.visibilityState==='visible' && this.active())void this.load();};
    const timer=setInterval(refresh,15000);document.addEventListener('visibilitychange',refresh);
    inject(DestroyRef).onDestroy(()=>{this.disposed=true;++this.version;clearInterval(timer);document.removeEventListener('visibilitychange',refresh);});
  }
  dayKey(date:Date):string{return date.getFullYear()+'-'+String(date.getMonth()+1).padStart(2,'0')+'-'+String(date.getDate()).padStart(2,'0');}
  async load():Promise<void>{
    if(!this.active() || this.busy() || this.disposed)return;
    const version=++this.version;this.loading.set(true);
    try{
      const preference=await firstValueFrom(this.http.get<{timeZone:string|null;zones:string[]}>('/api/auth/calendar/preferences'));
      if(this.disposed || version!==this.version)return;
      const nextZone=preference.timeZone || Intl.DateTimeFormat().resolvedOptions().timeZone;
      if(nextZone!==this.zone){this.zone=nextZone;this.selected.set(this.instantDay(new Date().toISOString()));const day=new Date(this.selected()+'T12:00');this.month.set(new Date(day.getFullYear(),day.getMonth(),1));}
      this.zones.set([...new Set([this.zone,...preference.zones])]);
      const month=this.month();const result=await firstValueFrom(this.http.get<CalendarList>('/api/auth/calendar',{params:{year:month.getFullYear(),month:month.getMonth()+1,timeZone:this.zone}}));
      if(this.disposed || version!==this.version)return;
      this.entries.set(result.items);this.connected.set(result.connected);this.ready.set(true);this.todayCounts.set({own:result.ownToday,partner:result.partnerToday});
      if(!result.connected){this.formOpened.set(false);this.entries.set([]);}
      if(this.error()==='Your schedule could not be refreshed. Try again.')this.error.set('');
    }catch(error){if(!this.disposed && version===this.version){this.entries.set([]);this.ready.set(false);this.todayCounts.set({own:0,partner:0});this.error.set('Your schedule could not be refreshed. Try again.');}}
    finally{if(version===this.version)this.loading.set(false);}
  }
  readonly all=computed<Appointment[]>(()=>[
    ...this.entries().map(x=>({id:'calendar-'+x.id+'-'+x.date,title:x.title,start:x.startUtc ?? undefined,end:x.endUtc ?? undefined,mine:x.mine,note:x.note ?? undefined,entry:x})),
    ...this.outings().filter(x=>!x.cancelled && !!x.venue?.startUtc).map(x=>({id:'outing-'+x.revision,title:(x.venue!.activity || 'Outing')+' · '+x.venue!.name,start:x.venue!.startUtc!,mine:x.mine,sharedDate:true})),
    ...this.contests().filter(x=>x.status==='accepted').map(x=>({id:'contest-'+x.id,title:'Cooking together',start:x.startUtc,mine:x.mine,sharedDate:true})),
  ].sort((a:Appointment,b:Appointment)=>(a.entry?.allDay ? 0 : 1)-(b.entry?.allDay ? 0 : 1) || Date.parse(a.start ?? '')-Date.parse(b.start ?? '')));
  readonly sharedToday=computed(() => { this.todayCounts(); const today=this.instantDay(new Date().toISOString()); return this.all().filter(x=>x.sharedDate && this.onDay(x,today)).length; });
  readonly todaySummary=computed(() => {
    const shared=this.sharedToday()>0; const counts=this.todayCounts();
    const own=shared || counts.own>0; const partner=shared || counts.partner>0;
    if(own && partner)return "Both of you have plans today";
    if(own)return this.ownName()+" has plans today";
    if(partner)return this.partnerName()+" has plans today";
    return "No plans for either of you today";
  });
  readonly agenda=computed(()=>{
    return this.all().filter(x=>this.onDay(x,this.selected()));
  });
  readonly week=computed(()=>{const date=new Date(this.selected()+'T12:00');const monday=date.getDate()-(date.getDay()+6)%7;return Array.from({length:7},(_,i)=>new Date(date.getFullYear(),date.getMonth(),monday+i,12));});
  readonly monthDays=computed(()=>{const date=this.month();const offset=(date.getDay()+6)%7;const count=new Date(date.getFullYear(),date.getMonth()+1,0).getDate();return [...Array<Date|null>(offset).fill(null),...Array.from({length:count},(_,i)=>new Date(date.getFullYear(),date.getMonth(),i+1,12))];});
  count(day:Date):number{
    return this.all().filter(x=>this.onDay(x,this.dayKey(day))).length;
  }
  instantDay(instant:string):string{const parts=new Intl.DateTimeFormat('en-US',{timeZone:this.zone,year:'numeric',month:'2-digit',day:'2-digit'}).formatToParts(new Date(instant));const part=(type:string)=>parts.find(x=>x.type===type)!.value;return part('year')+'-'+part('month')+'-'+part('day');}
  clockTime(instant:string|undefined):string{return instant ? new Intl.DateTimeFormat('en-GB',{timeZone:this.zone,hour:'2-digit',minute:'2-digit',hourCycle:'h23'}).format(new Date(instant)) : '';}
  private onDay(item:Appointment,key:string):boolean{if(item.entry?.allDay)return item.entry.date<=key && item.entry.endDate>key;if(!item.start)return false;return this.instantDay(item.start)<=key && this.instantDay(item.end ? new Date(Date.parse(item.end)-1).toISOString() : item.start)>=key;}
  choose(day:Date):void{this.selected.set(this.dayKey(day));const month=this.month();if(day.getFullYear()!==month.getFullYear() || day.getMonth()!==month.getMonth())this.month.set(new Date(day.getFullYear(),day.getMonth(),1));this.pager.reset();this.formOpened.set(false);this.removing.set(null);this.notice.set('');}
  moveWeek(direction:number):void{const date=new Date(this.selected()+'T12:00');this.choose(new Date(date.getFullYear(),date.getMonth(),date.getDate()+direction*7,12));}
  moveMonth(direction:number):void{const date=this.month();this.month.set(new Date(date.getFullYear(),date.getMonth()+direction,1));this.selected.set(this.dayKey(this.month()));this.pager.reset();this.formOpened.set(false);}
  goToday():void{this.choose(new Date(this.instantDay(new Date().toISOString())+'T12:00'));}
  add():void{this.editing.set(null);this.title='';this.startTime='09:00';this.endTime='10:00';this.entryDate=this.selected();this.endDate=this.entryDate;this.timeZone=this.zone;this.note='';this.allDay=false;this.yearly=false;this.error.set('');this.notice.set('');this.formOpened.set(true);queueMicrotask(()=>document.getElementById('schedule-title')?.focus());}
  edit(item:Appointment):void{const entry=item.entry;if(!entry?.mine)return;this.editing.set(entry);this.title=entry.title;this.note=entry.note ?? '';this.entryDate=entry.sourceDate;this.endDate=entry.sourceEndDate;this.startTime=entry.startTime;this.endTime=entry.endTime;this.timeZone=entry.timeZone;this.allDay=entry.allDay;this.yearly=entry.yearly;this.error.set('');this.formOpened.set(true);}
  async save():Promise<void>{
    if(this.busy())return;this.busy.set(true);++this.version;this.error.set('');
    try{const entry=this.editing();await this.auth.submit('calendar'+(entry ? '/'+entry.id : ''),{title:this.title,note:this.note,date:this.entryDate,endDate:this.entryDate,startTime:this.startTime,endTime:null,timeZone:this.timeZone,allDay:this.allDay,yearly:this.yearly,revision:entry?.revision});this.formOpened.set(false);this.notice.set('Appointment saved and shared with your person.');this.pager.reset();}
    catch(error){this.error.set(this.message(error));}
    finally{this.busy.set(false);await this.load();}
  }
  async remove(item:Appointment):Promise<void>{if(!item.entry?.mine || this.removing()!==item.id || this.busy())return;this.busy.set(true);++this.version;
    try{await this.auth.submit('calendar/'+item.entry.id+'/remove',{revision:item.entry.revision});this.removing.set(null);this.notice.set('Appointment removed from your shared calendar.');}catch(error){this.error.set(this.message(error));}finally{this.busy.set(false);await this.load();}
  }
  private message(error:unknown):string{return error instanceof HttpErrorResponse ? error.error?.title || 'Unable to update your calendar. Try again.' : 'Unable to update your calendar. Try again.';}
}
