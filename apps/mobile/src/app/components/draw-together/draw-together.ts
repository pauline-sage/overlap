import { BookRatingComponent } from '../read-together/book-rating/book-rating';
import { NavigationStateService, oneOf, navigationId, navigationBoolean } from '../../navigation-state.service';
import { Component, ElementRef, viewChild, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../auth.service';
import { LoadingIndicatorComponent } from '../loading-indicator/loading-indicator';
import { OutingChoiceComponent } from '../coffee-date/outing-choice/outing-choice';
import { DrawingCategory, DrawingList, DrawingPhoto, DrawingRound, drawingPixels } from './drawing-photo';

@Component({selector:'app-draw-together',imports:[BookRatingComponent,DatePipe,FormsModule,LoadingIndicatorComponent,OutingChoiceComponent],templateUrl:'./draw-together.html',styleUrl:'./draw-together.scss'})
export class DrawTogetherComponent {
  private readonly navigation=inject(NavigationStateService);
  readonly roundId=input(0);readonly albumOnly=input(false);readonly back=output<void>();
  readonly ownName=input('You');readonly partnerName=input('Your person');
  readonly rounds=signal<DrawingRound[]>([]);readonly selected=this.navigation.remember('draw.id',navigationId,0);readonly connected=signal(false);
  readonly busy=signal(false);readonly loading=signal(true);readonly error=signal('');readonly notice=signal('');
  readonly confirm=signal<'cancel'|'remove'|'photo'|null>(null);readonly page=this.navigation.remember('draw.page',(value):value is number=>navigationId(value) && value>0,1);
  readonly stopRound=signal<DrawingRound|null>(null);
  readonly deleteRound=signal<DrawingRound|null>(null);
  readonly historyOpen=this.navigation.remember('draw.history',navigationBoolean,false);
  readonly tick=signal(0);seconds='600';drawingCount='1';
  readonly categories=signal<DrawingCategory[]>([]);readonly categoryIds=signal<string[]>(['animals','nature','food','everyday']);
  readonly categoryRail=viewChild<ElementRef<HTMLElement>>('categoryRail');
  readonly categoryIndex=signal(0);
  moveCategory(direction:number):void{this.goToCategory(this.categoryIndex()+direction);}
  goToCategory(index:number):void{
    const rail=this.categoryRail()?.nativeElement;if(!rail)return;
    const next=Math.max(0,Math.min(this.categories().length-1,index));
    const card=rail.children[next] as HTMLElement|undefined;if(!card)return;
    this.categoryIndex.set(next);
    rail.scrollTo({left:card.offsetLeft-(rail.clientWidth-card.offsetWidth)/2,behavior:window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth'});
  }
  categoryScrolled(event:Event):void{
    const rail=event.target as HTMLElement;let nearest=0,distance=Infinity;
    Array.from(rail.children).forEach((card,index)=>{const delta=Math.abs((card as HTMLElement).offsetLeft-rail.scrollLeft-(rail.clientWidth-(card as HTMLElement).offsetWidth)/2);if(delta<distance){nearest=index;distance=delta;}});
    this.categoryIndex.set(nearest);
  }
  categoryKey(event:KeyboardEvent):void{
    if(event.key==='ArrowRight')this.moveCategory(1);else if(event.key==='ArrowLeft')this.moveCategory(-1);else if(event.key==='Home')this.goToCategory(0);else if(event.key==='End')this.goToCategory(this.categories().length-1);else return;
    event.preventDefault();
  }
  readonly counts=Array.from({length:10},(_,i)=>({value:String(i+1),label:String(i+1)}));
  readonly timers=[15,30,60,120,300,600,900,1200,1800,2700,3600].map(x=>({value:String(x),label:this.duration(x)}));
  duration(seconds:number):string{return seconds<60 ? seconds+' seconds' : seconds/60+(seconds===60 ? ' minute' : ' minutes');}
  private readonly http=inject(HttpClient);private readonly auth=inject(AuthService);
  private destroyed=false;private reading=false;private serverTime=Date.now();private sampledAt=performance.now();
  constructor(){
    effect(()=>{if(this.roundId())this.selected.set(this.roundId());untracked(()=>void this.load(true));});
    const timer=setInterval(()=>this.tick.update(x=>x+1),1000);
    const refresh=()=>{if(document.visibilityState==='visible')void this.load();};
    const poll=setInterval(refresh,3000);document.addEventListener('visibilitychange',refresh);
    inject(DestroyRef).onDestroy(()=>{this.destroyed=true;clearInterval(timer);clearInterval(poll);document.removeEventListener('visibilitychange',refresh);});
  }
  async load(feedback=false):Promise<void>{
    if(this.reading || (this.busy() && !feedback))return;
    this.reading=true;if(feedback)this.loading.set(true);
    try{
      const data=await firstValueFrom(this.http.get<DrawingList>('/api/auth/drawing'));
      if(this.destroyed)return;
      this.serverTime=Date.parse(data.serverUtc);this.sampledAt=performance.now();
      this.connected.set(data.connected);this.rounds.set(data.rounds);if(data.categories)this.categories.set(data.categories);
      if(this.error()==='Your drawings could not be refreshed. Try again.')this.error.set('');
      this.page.set(Math.min(this.page(),Math.max(1,Math.ceil(this.list().length/5))));
      if(this.selected() && !data.rounds.some(x=>x.id===this.selected()))this.selected.set(0);
      this.tick.update(x=>x+1);if(this.notice()==='Invitation sent.' && this.current() && this.state(this.current()!)!=='waiting')this.notice.set('');
    }catch{if(!this.destroyed)this.error.set('Your drawings could not be refreshed. Try again.');}
    finally{this.reading=false;if(!this.destroyed)this.loading.set(false);}
  }
  current():DrawingRound|undefined{return this.rounds().find(x=>x.id===this.selected());}
  remaining(round:DrawingRound):number{this.tick();return round.pausedSeconds != null ? round.pausedSeconds : round.endUtc ? Math.max(0,Math.ceil((Date.parse(round.endUtc)-this.serverTime-(performance.now()-this.sampledAt))/1000)) : 0;}
  state(round:DrawingRound):string{return round.status==='drawing' && this.remaining(round)===0 ? 'finished' : round.status;}
  timer(round:DrawingRound):string{const seconds=this.remaining(round);return Math.floor(seconds/60)+':'+String(seconds%60).padStart(2,'0');}
  unfinished():DrawingRound[]{return this.rounds().filter(x=>x.currentConnection!==false && this.state(x)!=='cancelled' && (this.state(x)!=='finished' || this.canNext(x) || !x.photos.some(p=>p.mine)));}
  toggleCategory(id:string):void{this.categoryIds.update(ids=>ids.includes(id) ? ids.filter(x=>x!==id) : [...ids,id]);}
  canNext(round:DrawingRound):boolean{return round.currentConnection!==false && this.state(round)==='finished' && !round.nextCreated && (round.drawingNumber ?? 1)<(round.drawingCount ?? 1);}
  partnerLabel(round:DrawingRound):string{return round.currentConnection===false ? 'Partner' : this.partnerName();}
  categoryNames(round:DrawingRound):string{return (round.categories ?? []).map(id=>this.categories().find(x=>x.id===id)?.title ?? id).join(' / ');}
  nextInSession(round:DrawingRound):DrawingRound|undefined{return round.currentConnection!==false && round.sessionId ? this.rounds().find(x=>x.sessionId===round.sessionId && (x.drawingNumber ?? 1)>(round.drawingNumber ?? 1) && this.state(x)!=='cancelled') : undefined;}
  finish():void{this.selected.set(0);this.navigation.write('draw.id',0);this.back.emit();}
  async nextDrawing():Promise<void>{const round=this.current();if(!round)return;await this.mutate(async()=>{const next=await this.auth.submit<{id:number}>('drawing/'+round.id+'/next',{revision:round.revision});this.selected.set(next.id);});}
  active():DrawingRound|undefined{return this.rounds().find(x=>['waiting','drawing'].includes(this.state(x)));}
  label(round:DrawingRound):string{
    switch(this.state(round)){
      case 'waiting':return round.mine ? 'Waiting for '+this.partnerName() : 'Ready for you to join';
      case 'drawing':return 'Drawing · '+this.timer(round)+' left';
      case 'paused':return 'Paused · '+this.timer(round)+' left';
      case 'finished':return round.photos.some(p=>p.mine) ? 'Your photo is saved' : 'Ready to share your drawing';
      default:return 'Ended';
    }
  }
  list():DrawingRound[]{return this.rounds().filter(x=>this.albumOnly() ? x.photos.length>0 : ['finished','cancelled'].includes(this.state(x)));}
  open(round:DrawingRound):void{this.selected.set(round.id);this.confirm.set(null);this.error.set('');this.notice.set('');}
  photoUrl(round:DrawingRound,photo:DrawingPhoto):string{return '/api/auth/drawing/'+round.id+'/photos/'+encodeURIComponent(photo.accountId)+'?v='+photo.revision;}
  requestStop(round:DrawingRound):void{this.stopRound.set({...round});this.confirm.set(null);}
  requestDelete(round:DrawingRound):void{
    if(!['finished','cancelled'].includes(this.state(round)) || (!this.albumOnly() && round.photos.length>0))return;
    this.historyOpen.set(true);this.deleteRound.set({...round});this.confirm.set(null);
  }
  async deleteConfirmed():Promise<void>{
    const round=this.deleteRound();if(!round)return;
    await this.mutate(async()=>{
      await this.auth.submit('drawing/'+round.id+'/remove',{revision:round.revision});
      this.selected.set(0);this.deleteRound.set(null);this.notice.set('Round deleted.');
    });
  }
  async stopAndChange():Promise<void>{
    const round=this.stopRound();if(!round)return;
    await this.mutate(async()=>{
      await this.auth.submit('drawing/'+round.id+'/cancel',{revision:round.revision});
      this.seconds=String(round.seconds);this.drawingCount=String(round.drawingCount ?? 1);if(round.categories)this.categoryIds.set([...round.categories]);this.selected.set(0);this.stopRound.set(null);
      this.notice.set('Round stopped. Choose a different timer or a new idea below.');
    });
  }
  async create():Promise<void>{await this.mutate(async()=>{const result=await this.auth.submit<{id:number}>('drawing',{seconds:Number(this.seconds),categories:this.categoryIds(),drawingCount:Number(this.drawingCount)});this.selected.set(result.id);this.notice.set('Invitation sent.');});}
  async action(action:'shuffle'|'join'|'decline'|'pause'|'resume'|'cancel'|'remove'):Promise<void>{
    const round=this.current();if(!round)return;
    await this.mutate(async()=>{await this.auth.submit('drawing/'+round.id+'/'+action,{revision:round.revision});this.confirm.set(null);if(action==='remove' || action==='pause' || action==='decline')this.selected.set(0);if(action==='decline')this.notice.set('Invitation declined.');});
  }
  async rating(photo:DrawingPhoto,score:number|null):Promise<void>{
    const round=this.current();if(!round)return;
    await this.mutate(()=>this.auth.submit('drawing/'+round.id+'/rating',{revision:round.revision,photoAccount:photo.accountId,photoRevision:photo.revision,score}));
  }
  async upload(event:Event):Promise<void>{
    const input=event.target as HTMLInputElement;const file=input.files?.[0];input.value='';
    const round=this.current();if(!file || !round)return;
    await this.mutate(async()=>{const pixels=await drawingPixels(file);await this.auth.submit('drawing/'+round.id+'/photo',{revision:round.revision,pixels});this.notice.set('Added to Albums.');});
  }
  async removePhoto():Promise<void>{const round=this.current();if(!round)return;await this.mutate(async()=>{await this.auth.submit('drawing/'+round.id+'/photo',{revision:round.revision,pixels:null});this.confirm.set(null);});}
  private async mutate(work:()=>Promise<unknown>):Promise<void>{
    if(this.busy() || this.loading())return;
    this.busy.set(true);this.error.set('');this.notice.set('');
    try{await work();}
    catch(error){this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Unable to save. Try again.' : error instanceof Error ? error.message : 'Unable to save. Try again.');}
    finally{await this.load(true);this.busy.set(false);}
  }
}
