import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { AuthService } from '../../auth.service';
import { FoodPreferencesComponent } from './food-preferences';
describe('Cooking food preferences',()=>{
  it('adds checked foods, saves removals and restores the list when saving fails',async()=>{
    let foods=['tuna'];
    const http={get:vi.fn(()=>of({ownPreferences:{avoidRedMeat:false,excludedIngredients:foods},shareWithPartner:false,partnerPreferences:null,partnerShared:false}))};
    const auth={submit:vi.fn(async(_path:string,body:{excludedIngredients:string[]})=>{foods=body.excludedIngredients;})};
    TestBed.configureTestingModule({imports:[FoodPreferencesComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth}]});
    const f=TestBed.createComponent(FoodPreferencesComponent);f.detectChanges();await vi.waitFor(()=>expect(f.componentInstance.busy()).toBe(false));
    const c=f.componentInstance;c.newFood='spinach, TUNA';await c.addFood();f.detectChanges();
    expect(c.excludedFoods.map(x=>x.toLowerCase())).toEqual(['tuna','spinach']);expect(c.newFood).toBe('');
    const checks=()=>Array.from(f.nativeElement.querySelectorAll('.excluded-foods input')) as HTMLInputElement[];
    expect(checks()).toHaveLength(2);expect(checks().every(x=>x.checked)).toBe(true);
    checks()[0].click();await vi.waitFor(()=>expect(c.busy()).toBe(false));f.detectChanges();
    expect(foods).toEqual(['spinach']);expect(checks()).toHaveLength(1);
    auth.submit.mockRejectedValueOnce(new Error('offline'));
    checks()[0].click();await vi.waitFor(()=>expect(c.busy()).toBe(false));f.detectChanges();
    expect(c.excludedFoods).toEqual(['spinach']);expect(checks()[0].checked).toBe(true);expect(c.error()).not.toBe('');f.destroy();
  });
  it('refreshes the partner list automatically without replacing owner edits',async()=>{
    vi.useFakeTimers();const visibility=vi.spyOn(document,'visibilityState','get').mockReturnValue('visible');
    const http={get:vi.fn().mockReturnValue(of({ownPreferences:{avoidRedMeat:false,excludedIngredients:['tuna']},shareWithPartner:false,partnerPreferences:null,partnerShared:false}))};
    TestBed.configureTestingModule({imports:[FoodPreferencesComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:{submit:vi.fn()}}]});
    const f=TestBed.createComponent(FoodPreferencesComponent);
    try{
      f.detectChanges();await vi.advanceTimersByTimeAsync(0);
      f.componentInstance.excludedIngredients='spinach';f.componentInstance.shareWithPartner=true;
      http.get.mockReturnValue(of({ownPreferences:{avoidRedMeat:false,excludedIngredients:['tuna']},shareWithPartner:false,partnerPreferences:{avoidRedMeat:true,excludedIngredients:['olives']},partnerShared:true}));
      await vi.advanceTimersByTimeAsync(10000);f.detectChanges();
      expect(f.nativeElement.textContent).toContain('olives');expect(f.componentInstance.excludedIngredients).toBe('spinach');expect(f.componentInstance.shareWithPartner).toBe(true);
      http.get.mockReturnValue(of({ownPreferences:null,shareWithPartner:false,partnerPreferences:null,partnerShared:false}));await f.componentInstance.load(true);f.detectChanges();
      expect(f.nativeElement.textContent).not.toContain('olives');expect(f.nativeElement.textContent).toContain("Your partner's list is private");
    }finally{f.destroy();visibility.mockRestore();vi.useRealTimers();}
  });
  it('reloads the saved owner list and reports a failed reload without claiming success',async()=>{
    const http={get:vi.fn().mockReturnValue(of({ownPreferences:null,shareWithPartner:false,partnerPreferences:null,partnerShared:false}))};const auth={submit:vi.fn().mockResolvedValue({})};
    TestBed.configureTestingModule({imports:[FoodPreferencesComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth}]});
    const f=TestBed.createComponent(FoodPreferencesComponent);f.detectChanges();await vi.waitFor(()=>expect(f.componentInstance.busy()).toBe(false));
    const c=f.componentInstance;c.excludedIngredients=' tuna, tuna ';
    http.get.mockReturnValue(of({ownPreferences:{avoidRedMeat:false,excludedIngredients:['tuna']},shareWithPartner:false,partnerPreferences:null,partnerShared:false}));await c.save();expect(c.excludedIngredients).toBe('tuna');
    http.get.mockReturnValue(throwError(()=>new Error('offline')));await c.save();expect(c.notice()).toBe('');expect(c.error()).not.toBe('');f.destroy();
  });
  it('saves the owner list and explicit sharing choice, while hiding an unshared partner list',async()=>{
    const http={get:vi.fn().mockReturnValue(of({ownPreferences:null,shareWithPartner:false,partnerPreferences:null,partnerShared:false}))};const auth={submit:vi.fn().mockResolvedValue({})};
    TestBed.configureTestingModule({imports:[FoodPreferencesComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth}]});
    const f=TestBed.createComponent(FoodPreferencesComponent);f.detectChanges();await vi.waitFor(()=>expect(f.componentInstance.busy()).toBe(false));f.detectChanges();
    expect(f.nativeElement.textContent).toContain("Your partner's list is private");const c=f.componentInstance;expect(c.shareWithPartner).toBe(false);
    c.avoidRedMeat=true;c.excludedIngredients=' tuna, spinach ';c.shareWithPartner=true;
    const saved=vi.fn();c.saved.subscribe(saved);await c.save();
    expect(auth.submit).toHaveBeenCalledExactlyOnceWith('cooking/preferences',{avoidRedMeat:true,excludedIngredients:['tuna','spinach'],shareWithPartner:true});expect(saved).toHaveBeenCalledOnce();
    http.get.mockReturnValue(of({ownPreferences:null,shareWithPartner:false,partnerPreferences:{avoidRedMeat:false,excludedIngredients:['olives']},partnerShared:true}));await c.load();f.detectChanges();expect(f.nativeElement.textContent).toContain('olives');f.destroy();
  });
});
