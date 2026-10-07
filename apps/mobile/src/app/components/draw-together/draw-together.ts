import { Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../auth.service';
import { LoadingIndicatorComponent } from '../loading-indicator/loading-indicator';
import { OutingChoiceComponent } from '../coffee-date/outing-choice/outing-choice';
import { DrawingList, DrawingPhoto, DrawingRound, drawingPixels } from './drawing-photo';

@Component({selector:'app-draw-together',imports:[DatePipe,FormsModule,LoadingIndicatorComponent,OutingChoiceComponent],templateUrl:'./draw-together.html',styleUrl:'./draw-together.scss'})
export class DrawTogetherComponent {
  readonly roundId=input(0);readonly albumOnly=input(false);readonly back=output<void>();
  readonly ownName=input('You');readonly partnerName=input('Your person');
  readonly rounds=signal<DrawingRound[]>([]);readonly selected=signal(0);readonly connected=signal(false);
  readonly busy=signal(false);readonly loading=signal(true);readonly error=signal('');readonly notice=signal('');
  readonly confirm=signal<'cancel'|'remove'|'photo'|null>(null);readonly page=signal(1);
  readonly tick=signal(0);seconds='600';
  readonly timers=[15,30,60,120,300,600,900,1200,1800,2700,3600].map(x=>({value:String(x),label:this.duration(x)}));
  duration(seconds:number):string{return seconds<60 ? seconds+' seconds' : seconds/60+(seconds===60 ? ' minute' : ' minutes');}
  private readonly http=inject(HttpClient);private readonly auth=inject(AuthService);
  private destroyed=false;private reading=false;private serverTime=Date.now();private sampledAt=performance.now();
  constructor(){
    effect(()=>{this.selected.set(this.roundId());void this.load(true);});
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
      this.connected.set(data.connected);this.rounds.set(data.rounds);
      this.page.set(Math.min(this.page(),Math.max(1,Math.ceil(this.list().length/10))));
      if(this.selected() && !data.rounds.some(x=>x.id===this.selected()))this.selected.set(0);
      this.tick.update(x=>x+1);
    }catch{if(!this.destroyed)this.error.set('Your drawings could not be refreshed. Try again.');}
    finally{this.reading=false;if(!this.destroyed)this.loading.set(false);}
  }
  current():DrawingRound|undefined{return this.rounds().find(x=>x.id===this.selected());}
  remaining(round:DrawingRound):number{this.tick();return round.endUtc ? Math.max(0,Math.ceil((Date.parse(round.endUtc)-this.serverTime-(performance.now()-this.sampledAt))/1000)) : 0;}
  state(round:DrawingRound):string{return round.status==='drawing' && this.remaining(round)===0 ? 'finished' : round.status;}
  timer(round:DrawingRound):string{const seconds=this.remaining(round);return Math.floor(seconds/60)+':'+String(seconds%60).padStart(2,'0');}
  activeRound():boolean{return this.rounds().some(x=>['waiting','drawing'].includes(this.state(x)));}
  list():DrawingRound[]{return this.rounds().filter(x=>!this.albumOnly() || x.photos.length>0);}
  open(round:DrawingRound):void{this.selected.set(round.id);this.confirm.set(null);this.error.set('');this.notice.set('');}
  photoUrl(round:DrawingRound,photo:DrawingPhoto):string{return '/api/auth/drawing/'+round.id+'/photos/'+encodeURIComponent(photo.accountId)+'?v='+photo.revision;}
  async create():Promise<void>{await this.mutate(async()=>{const result=await this.auth.submit<{id:number}>('drawing',{seconds:Number(this.seconds)});this.selected.set(result.id);});}
  async action(action:'shuffle'|'join'|'cancel'|'remove'):Promise<void>{
    const round=this.current();if(!round)return;
    await this.mutate(async()=>{await this.auth.submit('drawing/'+round.id+'/'+action,{revision:round.revision});this.confirm.set(null);if(action==='remove')this.selected.set(0);});
  }
  async rating(photo:DrawingPhoto,score:number|null):Promise<void>{
    const round=this.current();if(!round)return;
    await this.mutate(()=>this.auth.submit('drawing/'+round.id+'/rating',{revision:round.revision,photoAccount:photo.accountId,photoRevision:photo.revision,score}));
  }
  async upload(event:Event):Promise<void>{
    const input=event.target as HTMLInputElement;const file=input.files?.[0];input.value='';
    const round=this.current();if(!file || !round)return;
    await this.mutate(async()=>{const pixels=await drawingPixels(file);await this.auth.submit('drawing/'+round.id+'/photo',{revision:round.revision,pixels});this.notice.set('Your drawing is saved in your shared album.');});
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
