import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { AuthService } from '../../auth.service';
import { FoodPreferencesComponent } from './food-preferences';
describe('Cooking food preferences',()=>{
  it('saves the owner list and explicit sharing choice, while hiding an unshared partner list',async()=>{
    const http={get:vi.fn().mockReturnValue(of({ownPreferences:null,shareWithPartner:false,partnerPreferences:null,partnerShared:false}))};const auth={submit:vi.fn().mockResolvedValue({})};
    TestBed.configureTestingModule({imports:[FoodPreferencesComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth}]});
    const f=TestBed.createComponent(FoodPreferencesComponent);f.detectChanges();await f.whenStable();f.detectChanges();
    expect(f.nativeElement.textContent).toContain("Your partner's list is private");const c=f.componentInstance;expect(c.shareWithPartner).toBe(false);
    c.avoidRedMeat=true;c.excludedIngredients=' tuna, spinach ';c.shareWithPartner=true;
    const saved=vi.fn();c.saved.subscribe(saved);await c.save();
    expect(auth.submit).toHaveBeenCalledExactlyOnceWith('cooking/preferences',{avoidRedMeat:true,excludedIngredients:['tuna','spinach'],shareWithPartner:true});expect(saved).toHaveBeenCalledOnce();
    http.get.mockReturnValue(of({ownPreferences:null,shareWithPartner:false,partnerPreferences:{avoidRedMeat:false,excludedIngredients:['olives']},partnerShared:true}));await c.load();f.detectChanges();expect(f.nativeElement.textContent).toContain('olives');f.destroy();
  });
});
