import { Component, inject, input, output, signal, computed, effect, OnInit, DestroyRef } from '@angular/core';
import { OutingChoiceComponent, OutingCalendarComponent } from '../coffee-date/outing-controls';
import { DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../auth.service';
import { SharingService } from '../nav-bar/sharing/sharing.service';
import { DateCardsComponent } from '../date-cards/date-cards';
import { LoadingIndicatorComponent } from '../loading-indicator';

interface FoodPreferences {avoidRedMeat:boolean;excludedIngredients:string[];}
interface ContestRecipe {title:string;ingredients:string[];instructions:string[];sourceUrl:string;photo?:{url:string;author:string;license:string;sourceUrl:string};}
@Component({selector:'app-cooking-contests',imports:[DatePipe,FormsModule,DateCardsComponent,LoadingIndicatorComponent,OutingChoiceComponent,OutingCalendarComponent],templateUrl:'./cooking-contests.html',styleUrls:['./cooking.scss','./cooking-contests.scss']})
export class CookingContestsComponent implements OnInit {
  readonly contestId=input(0);
  readonly selectedId=signal(0);
  readonly contest=computed(()=>this.sharing.state()?.cookingContests?.find(x=>x.id===this.selectedId()));
  readonly recipe=signal<ContestRecipe|null>(null);
  readonly ownPreferences=signal<FoodPreferences|null>(null);
  readonly partnerPreferences=signal<FoodPreferences|null>(null);
  readonly partnerConfirmed=signal(false);
  readonly managePreferences=output<void>();
  readonly preferencesReady=computed(()=>!!this.ownPreferences() && this.partnerConfirmed());

  readonly now=signal(Date.now());
  readonly revealedUtc=signal<string|null>(null);
  readonly redrawPending=signal(false);readonly redrawMine=signal(false);readonly drawRevision=signal('');
  private clockOffset=0;
  readonly countdown=computed(()=>{const seconds=Math.max(0,Math.ceil((Date.parse(this.contest()?.startUtc || '')-this.now())/1000));if(!Number.isFinite(seconds))return '';const days=Math.floor(seconds/86400);return (days ? days+'d ' : '')+[Math.floor(seconds/3600)%24,Math.floor(seconds/60)%60,seconds%60].map(x=>String(x).padStart(2,'0')).join(':');});
  readonly countdownParts=computed(()=>{const values=this.countdown().split(/d |:/);if(values.length===3)values.unshift('00');return ['Days','Hours','Minutes','Seconds'].map((label,i)=>({label,value:(values[i] || '0').padStart(2,'0')}));});
  readonly unlocked=computed(()=>this.contest()?.status==='accepted' && this.now()>=Date.parse(this.contest()!.startUtc));
  readonly shuffling=computed(()=>!!this.revealedUtc() && this.now()<Date.parse(this.revealedUtc()!)+8000);
  readonly shuffleFrames=['Mix','Chop','Bake','Taste'];
  readonly shuffleFrame=computed(()=>this.shuffleFrames[Math.max(0,Math.floor((this.now()-Date.parse(this.revealedUtc() || ''))/250))%4] || 'Mix');
  private readonly http=inject(HttpClient);
  private detailVersion=0;
  private readonly detailKey=computed(()=>this.selectedId()+':'+(this.contest()?.revision || ''));
  constructor(){
    effect(()=>{const key=this.detailKey();if(key.split(':')[0]!=='0')void this.loadRecipe(this.selectedId());});
    let tick=0;const timer=setInterval(()=>{this.now.set(Date.now()+this.clockOffset);if(++tick%(this.redrawPending()?20:this.recipe()?60:this.unlocked()?12:60)===0 && document.visibilityState==='visible' && this.selectedId() && !this.busy())void this.loadRecipe(this.selectedId());},250);
    inject(DestroyRef).onDestroy(()=>{clearInterval(timer);++this.detailVersion;});
  }
  ngOnInit():void {this.selectedId.set(this.contestId());}
  select(id:number):void {++this.detailVersion;this.recipe.set(null);this.revealedUtc.set(null);this.selectedId.set(id);this.error.set('');}
  async loadRecipe(id:number):Promise<void> {
    const version=++this.detailVersion;
    try {
      const result=await firstValueFrom(this.http.get<{recipe:ContestRecipe|null;revealedUtc?:string|null;serverUtc?:string;ownPreferences?:FoodPreferences|null;partnerPreferences?:FoodPreferences|null;partnerConfirmed?:boolean;revision?:string;redrawPending?:boolean;redrawMine?:boolean}>('/api/auth/cooking/contests/'+id));
      if(version===this.detailVersion && id===this.selectedId()){
        if(result.serverUtc)this.clockOffset=Date.parse(result.serverUtc)-Date.now();this.now.set(Date.now()+this.clockOffset);
        this.ownPreferences.set(result.ownPreferences || null);this.partnerPreferences.set(result.partnerPreferences || null);
        this.partnerConfirmed.set(result.partnerConfirmed ?? !!result.partnerPreferences);
        this.drawRevision.set(result.revision || this.contest()?.revision || "");this.redrawPending.set(result.redrawPending || false);this.redrawMine.set(result.redrawMine || false);
        this.recipe.set(result.recipe);this.revealedUtc.set(result.revealedUtc || null);
      }
    } catch{if(version===this.detailVersion)this.error.set('This contest is unavailable. It may have been removed.');}
  }
  async redraw(action:'request'|'accept'|'decline'|'withdraw'):Promise<void>{
    if(this.busy() || !this.selectedId())return;this.busy.set(true);this.error.set('');
    try{await this.auth.submit('cooking/contests/'+this.selectedId()+'/redraw',{revision:this.drawRevision(),action});await this.loadRecipe(this.selectedId());await this.sharing.load(true);}
    catch(error){this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Unable to update the redraw request.' : 'Unable to update the redraw request.');await this.loadRecipe(this.selectedId());}
    finally{this.busy.set(false);}
  }
  async shuffle():Promise<void> {
    const contest=this.contest();if(!contest || !this.unlocked() || !this.preferencesReady() || this.busy() || this.recipe())return;this.busy.set(true);this.error.set('');
    try{await this.auth.submit('cooking/contests/'+contest.id+'/shuffle',{});await this.loadRecipe(contest.id);await this.sharing.load(true);}
    catch(error){this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Unable to shuffle. Try again.' : 'Unable to shuffle. Try again.');}
    finally{this.busy.set(false);}
  }
  readonly back=output<void>();
  readonly sharing=inject(SharingService);
  private readonly auth=inject(AuthService);
  readonly busy=signal(false);
  readonly error=signal('');
  readonly notice=signal('');
  readonly zone=Intl.DateTimeFormat().resolvedOptions().timeZone;
  date='';hour='12';minute='00';
  availableMinutes='60';
  readonly hourOptions=Array.from({length:24},(_,i)=>{const value=String(i).padStart(2,'0');return {value,label:value};});
  readonly minuteOptions=['00','15','30','45'].map(value=>({value,label:value}));
  readonly availabilityOptions=[30,45,60,90,120].map(value=>({value:String(value),label:'About '+(value<60 ? value+' minutes' : value===60 ? '1 hour' : value===90 ? '1 hour 30 minutes' : '2 hours')}));
  async invite():Promise<void> {
    if(this.busy())return;this.busy.set(true);this.error.set('');this.notice.set('');
    try {
      await this.auth.submit('cooking/contests',{localStart:this.date+'T'+this.hour+':'+this.minute,timeZone:this.zone,availableMinutes:Number(this.availableMinutes)});
      this.notice.set('Invitation sent. Once your partner accepts, your contest will appear in Dates.');
      this.date='';await this.sharing.load(true);
    } catch(error){this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Unable to send your invitation.' : 'Unable to send your invitation.');}
    finally{this.busy.set(false);}
  }
}
