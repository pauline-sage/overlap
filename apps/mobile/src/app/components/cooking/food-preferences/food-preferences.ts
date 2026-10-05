import { Component, inject, output, signal, OnInit, DestroyRef } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../../auth.service';
import { LoadingIndicatorComponent } from '../../loading-indicator/loading-indicator';
interface Preferences {avoidRedMeat:boolean;excludedIngredients:string[];}
interface PreferenceLists {ownPreferences:Preferences|null;shareWithPartner:boolean;partnerPreferences:Preferences|null;partnerShared:boolean;}
@Component({selector:'app-food-preferences',imports:[FormsModule,LoadingIndicatorComponent],templateUrl:'./food-preferences.html',styleUrl:'../cooking.scss'})
export class FoodPreferencesComponent implements OnInit {
  readonly back=output<void>();readonly saved=output<void>();
  private readonly http=inject(HttpClient);private readonly auth=inject(AuthService);
  readonly busy=signal(false);readonly error=signal('');readonly notice=signal('');
  readonly partner=signal<Preferences|null>(null);readonly partnerShared=signal(false);
  avoidRedMeat=false;excludedIngredients='';shareWithPartner=false;newFood='';
  get excludedFoods():string[]{return [...new Map(this.excludedIngredients.split(',').map(x=>x.trim()).filter(Boolean).map(x=>[x.toLowerCase(),x])).values()];}
  async addFood():Promise<void>{
    if(this.busy())return;
    const additions=this.newFood.split(',').map(x=>x.trim()).filter(Boolean);
    if(!additions.length)return;
    const previous=this.excludedIngredients;
    const foods=[...new Map([...this.excludedFoods,...additions].map(x=>[x.toLowerCase(),x])).values()];
    if(foods.length>12 || foods.some(x=>x.length>50)){this.error.set('Choose up to 12 foods, with at most 50 characters per food.');return;}
    this.excludedIngredients=foods.join(', ');
    if(await this.save(false))this.newFood='';else this.excludedIngredients=previous;
  }
  async removeFood(food:string,event?:Event):Promise<void>{
    if(event)(event.target as HTMLInputElement).checked=true;
    if(this.busy())return;
    const previous=this.excludedIngredients;
    this.excludedIngredients=this.excludedFoods.filter(x=>x!==food).join(', ');
    if(!await this.save(false))this.excludedIngredients=previous;
  }
  private loading=false;private destroyed=false;private version=0;
  constructor(){
    const refresh=()=>{if(document.visibilityState==='visible')void this.load(true);};
    const timer=setInterval(refresh,10000);document.addEventListener('visibilitychange',refresh);
    inject(DestroyRef).onDestroy(()=>{this.destroyed=true;clearInterval(timer);document.removeEventListener('visibilitychange',refresh);});
  }
  ngOnInit():void{void this.load();}
  private async fetch(updateOwn:boolean):Promise<void>{
    const version=++this.version;
    const result=await firstValueFrom(this.http.get<PreferenceLists>('/api/auth/cooking/preferences'));
    if(this.destroyed || version!==this.version)return;
    if(updateOwn){
      this.avoidRedMeat=result.ownPreferences?.avoidRedMeat || false;this.excludedIngredients=result.ownPreferences?.excludedIngredients.join(', ') || '';this.shareWithPartner=result.shareWithPartner;
    }
    this.partner.set(result.partnerShared ? result.partnerPreferences : null);this.partnerShared.set(result.partnerShared);
    if(this.error()==='Food preferences could not be refreshed. Try again.')this.error.set('');
  }
  async load(partnerOnly=false):Promise<void>{
    if(this.loading || this.busy())return;this.loading=true;
    if(!partnerOnly){this.busy.set(true);this.error.set('');}
    try{await this.fetch(!partnerOnly);}
    catch{if(!this.destroyed)this.error.set('Food preferences could not be refreshed. Try again.');}
    finally{this.loading=false;if(!partnerOnly)this.busy.set(false);}
  }
  async save(includeDraft=true):Promise<boolean>{
    if(this.busy())return false;
    const additions=includeDraft ? this.newFood.split(',').map(x=>x.trim()).filter(Boolean) : [];
    const foods=[...new Map([...this.excludedFoods,...additions].map(x=>[x.toLowerCase(),x])).values()];
    if(foods.length>12 || foods.some(x=>x.length>50)){this.error.set('Choose up to 12 foods, with at most 50 characters per food.');return false;}
    ++this.version;this.busy.set(true);this.error.set('');this.notice.set('');let stored=false;
    try{await this.auth.submit('cooking/preferences',{avoidRedMeat:this.avoidRedMeat,excludedIngredients:foods,shareWithPartner:this.shareWithPartner});stored=true;this.excludedIngredients=foods.join(', ');if(includeDraft)this.newFood='';await this.fetch(true);if(!this.destroyed){this.notice.set('Your food preferences are saved.');this.saved.emit();}}
    catch(error){this.error.set(stored ? 'Your preferences were saved, but could not be refreshed. Reopen Food preferences to retry.' : error instanceof HttpErrorResponse ? error.error?.title || 'Unable to save preferences.' : 'Unable to save preferences.');}finally{this.busy.set(false);}
    return stored;
  }
}
