import { Component, inject, output, signal, OnInit } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../auth.service';
import { LoadingIndicatorComponent } from '../loading-indicator';
interface Preferences {avoidRedMeat:boolean;excludedIngredients:string[];}
@Component({selector:'app-food-preferences',imports:[FormsModule,LoadingIndicatorComponent],templateUrl:'./food-preferences.html',styleUrl:'./cooking.scss'})
export class FoodPreferencesComponent implements OnInit {
  readonly back=output<void>();readonly saved=output<void>();
  private readonly http=inject(HttpClient);private readonly auth=inject(AuthService);
  readonly busy=signal(false);readonly error=signal('');readonly notice=signal('');
  readonly partner=signal<Preferences|null>(null);readonly partnerShared=signal(false);
  avoidRedMeat=false;excludedIngredients='';shareWithPartner=false;
  ngOnInit():void{void this.load();}
  async load():Promise<void>{
    this.busy.set(true);this.error.set('');
    try{const result=await firstValueFrom(this.http.get<{ownPreferences:Preferences|null;shareWithPartner:boolean;partnerPreferences:Preferences|null;partnerShared:boolean}>('/api/auth/cooking/preferences'));
      this.avoidRedMeat=result.ownPreferences?.avoidRedMeat || false;this.excludedIngredients=result.ownPreferences?.excludedIngredients.join(', ') || '';this.shareWithPartner=result.shareWithPartner;
      this.partner.set(result.partnerPreferences);this.partnerShared.set(result.partnerShared);
    }catch{this.error.set('Food preferences are unavailable. Try again.');}finally{this.busy.set(false);}
  }
  async save():Promise<void>{
    if(this.busy())return;this.busy.set(true);this.error.set('');this.notice.set('');
    try{await this.auth.submit('cooking/preferences',{avoidRedMeat:this.avoidRedMeat,excludedIngredients:this.excludedIngredients.split(',').map(x=>x.trim()).filter(Boolean),shareWithPartner:this.shareWithPartner});await this.load();this.notice.set('Your food preferences are saved.');this.saved.emit();}
    catch(error){this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Unable to save preferences.' : 'Unable to save preferences.');}finally{this.busy.set(false);}
  }
}
