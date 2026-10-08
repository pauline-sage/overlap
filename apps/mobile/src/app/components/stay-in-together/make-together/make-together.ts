import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { DrawTogetherComponent } from '../../draw-together/draw-together';
import { DrawingList } from '../../draw-together/drawing-photo';
import { LoadingIndicatorComponent } from '../../loading-indicator/loading-indicator';
import { CookingComponent } from '../../cooking/cooking';
import { StayInActivity } from '../stay-in.types';
@Component({selector:'app-make-together',imports:[CookingComponent,DrawTogetherComponent,LoadingIndicatorComponent],templateUrl:'./make-together.html',styleUrl:'../activity-section.scss'})
export class MakeTogetherComponent {
  readonly active=input(false);readonly activity=input<StayInActivity>('menu');readonly ownName=input('Me');readonly partnerName=input('Your person');readonly recipeId=input(0);readonly contestId=input(0);readonly drawingRoundId=input(0);
  readonly choose=output<StayInActivity>();readonly back=output<void>();
  readonly drawingCount=signal(0);readonly loading=signal(false);readonly error=signal('');
  private readonly http=inject(HttpClient);private reading=false;private destroyed=false;
  constructor(){
    effect(()=>{if(this.active() && this.activity()==='menu')untracked(()=>void this.refresh(true));});
    const refresh=()=>{if(this.active() && this.activity()==='menu' && document.visibilityState==='visible')void this.refresh();};
    const poll=setInterval(refresh,15000);document.addEventListener('visibilitychange',refresh);
    inject(DestroyRef).onDestroy(()=>{this.destroyed=true;clearInterval(poll);document.removeEventListener('visibilitychange',refresh);});
  }
  async refresh(feedback=false):Promise<void>{
    if(this.reading)return;this.reading=true;if(feedback)this.loading.set(true);
    try{
      const data=await firstValueFrom(this.http.get<DrawingList>('/api/auth/drawing'));
      if(!this.destroyed){this.drawingCount.set(data.rounds.filter(x=>x.currentConnection!==false && x.status!=='cancelled' && (x.status!=='finished' || (!x.nextCreated && (x.drawingNumber ?? 1)<(x.drawingCount ?? 1)) || !x.photos.some(p=>p.mine))).length);this.error.set('');}
    }catch{if(!this.destroyed)this.error.set('Drawing progress could not be refreshed.');}
    finally{this.reading=false;if(!this.destroyed)this.loading.set(false);}
  }
}
