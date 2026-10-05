import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { signal } from '@angular/core';
import { AuthService } from '../../auth.service';
import { SharingService } from '../nav-bar/sharing/sharing.service';
import { CookingContestsComponent } from './cooking-contests';
describe('Cooking contest invitations',()=>{
  it('requests a redraw without replacing the current recipe and lets the partner consent',async()=>{
    const contest={id:7,revision:'initial',status:'accepted',startUtc:new Date(Date.now()-20000).toISOString(),availableMinutes:60};
    const recipe={title:'Flan',ingredients:['Milk'],instructions:['Mix'],sourceUrl:'https://en.wikibooks.org'};
    const http={get:vi.fn().mockReturnValue(of({recipe,revision:'current',revealedUtc:new Date(Date.now()-10000).toISOString(),redrawPending:false}))};const auth={submit:vi.fn().mockResolvedValue({})};
    const sharing={state:signal({connected:true,cookingContests:[contest]}),busy:signal(false),error:signal(''),load:vi.fn().mockResolvedValue(undefined)};
    TestBed.configureTestingModule({imports:[CookingContestsComponent],providers:[{provide:HttpClient,useValue:{get:(url:string)=>url.includes('/cooking/memories/') ? of({contestId:7,revision:'memory',completed:false,archived:false,recipe:{title:'Flan',ingredients:['Milk'],instructions:['Mix'],sourceUrl:'https://en.wikibooks.org'},photos:[]}) : http.get(url)}},{provide:AuthService,useValue:auth},{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(CookingContestsComponent);f.componentRef.setInput('contestId',7);f.detectChanges();await f.whenStable();
    http.get.mockReturnValue(of({recipe,revision:'request',revealedUtc:new Date(Date.now()-10000).toISOString(),redrawPending:true,redrawMine:true}));await f.componentInstance.redraw('request');f.detectChanges();
    expect(auth.submit).toHaveBeenCalledExactlyOnceWith('cooking/contests/7/redraw',{revision:'current',action:'request'});expect(f.componentInstance.recipe()?.title).toBe('Flan');expect(f.nativeElement.textContent).toContain('Waiting for your partner to agree');
    http.get.mockReturnValue(of({recipe,revision:'request',revealedUtc:new Date(Date.now()-10000).toISOString(),redrawPending:true,redrawMine:false}));await f.componentInstance.loadRecipe(7);f.detectChanges();expect(f.nativeElement.textContent).toContain('Yes, shuffle again');expect(f.nativeElement.textContent).toContain('Keep this recipe');f.destroy();
  });
  it('updates my saved status and respects readiness while the partner list is private',async()=>{
    const contest={id:7,revision:'accepted',status:'accepted',startUtc:new Date(Date.now()-1000).toISOString(),availableMinutes:60};
    const http={get:vi.fn().mockReturnValue(of({recipe:null,ownPreferences:null,partnerPreferences:null,partnerConfirmed:false}))};const auth={submit:vi.fn()};
    const sharing={state:signal({connected:true,cookingContests:[contest]}),busy:signal(false),error:signal(''),load:vi.fn()};
    TestBed.configureTestingModule({imports:[CookingContestsComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth},{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(CookingContestsComponent);f.componentRef.setInput('contestId',7);f.detectChanges();await f.whenStable();
    const c=f.componentInstance;await c.shuffle();expect(auth.submit).not.toHaveBeenCalled();
    http.get.mockReturnValue(of({recipe:null,ownPreferences:{avoidRedMeat:true,excludedIngredients:['tuna']},partnerPreferences:null,partnerConfirmed:false}));await c.loadRecipe(7);f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Your preferences are saved. Waiting for your partner.');expect(f.nativeElement.querySelector('#excluded-ingredients')).toBeNull();
    http.get.mockReturnValue(of({recipe:null,ownPreferences:{avoidRedMeat:true,excludedIngredients:['tuna']},partnerPreferences:null,partnerConfirmed:true}));await c.loadRecipe(7);f.detectChanges();
    expect(c.preferencesReady()).toBe(true);expect(f.nativeElement.textContent).toContain('Both food preferences are saved. You are ready.');f.destroy();
  });
  it('joins a partner-triggered shuffle through visible contest polling',async()=>{
    vi.useFakeTimers();vi.setSystemTime(new Date('2026-10-10T18:30:00Z'));
    const contest={id:7,revision:'accepted',status:'accepted',startUtc:'2026-10-10T18:30:00Z',availableMinutes:60};
    const http={get:vi.fn().mockReturnValue(of({recipe:null,ownPreferences:{avoidRedMeat:false,excludedIngredients:[]},partnerPreferences:{avoidRedMeat:false,excludedIngredients:[]}}))};const auth={submit:vi.fn()};
    const sharing={state:signal({connected:true,cookingContests:[contest]}),busy:signal(false),error:signal(''),load:vi.fn()};
    TestBed.configureTestingModule({imports:[CookingContestsComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth},{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(CookingContestsComponent);f.componentRef.setInput('contestId',7);
    const visible=vi.spyOn(document,'visibilityState','get').mockReturnValue('visible');
    try{
      f.detectChanges();await Promise.resolve();
      http.get.mockReturnValue(of({recipe:{title:'Flan',ingredients:['Milk'],instructions:['Mix'],sourceUrl:'https://en.wikibooks.org'},revealedUtc:'2026-10-10T18:30:00Z',ownPreferences:{avoidRedMeat:false,excludedIngredients:[]},partnerPreferences:{avoidRedMeat:false,excludedIngredients:[]}}));
      await vi.advanceTimersByTimeAsync(3000);f.detectChanges();
      expect(f.componentInstance.shuffling()).toBe(true);expect(f.componentInstance.recipe()?.title).toBe('Flan');expect(auth.submit).not.toHaveBeenCalled();
      await vi.advanceTimersByTimeAsync(5000);f.detectChanges();expect(f.nativeElement.textContent).toContain('Milk');
    }finally{f.destroy();visible.mockRestore();vi.useRealTimers();}
  });
  it('counts down, unlocks at the shared start and reveals one persisted draw',async()=>{
    vi.useFakeTimers();vi.setSystemTime(new Date('2026-10-10T18:29:59Z'));
    const contest={id:7,revision:'current',status:'accepted',startUtc:'2026-10-10T18:30:00Z',availableMinutes:60};
    const recipe={title:'Flan',ingredients:['Milk'],instructions:['Mix'],sourceUrl:'https://en.wikibooks.org'};
    const http={get:vi.fn().mockReturnValue(of({recipe:null,ownPreferences:{avoidRedMeat:false,excludedIngredients:[]},partnerPreferences:{avoidRedMeat:false,excludedIngredients:[]}}))};
    const auth={submit:vi.fn().mockResolvedValue({})};const sharing={state:signal({connected:true,cookingContests:[contest]}),busy:signal(false),error:signal(''),load:vi.fn().mockResolvedValue(undefined)};
    TestBed.configureTestingModule({imports:[CookingContestsComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth},{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(CookingContestsComponent);f.componentRef.setInput('contestId',7);
    try {
      f.detectChanges();await Promise.resolve();f.detectChanges();
      expect(f.componentInstance.unlocked()).toBe(false);expect(f.nativeElement.querySelector('[role=timer]').getAttribute('aria-label')).toContain('00:00:01');await f.componentInstance.shuffle();expect(auth.submit).not.toHaveBeenCalled();
      await vi.advanceTimersByTimeAsync(1000);f.detectChanges();expect(f.componentInstance.unlocked()).toBe(true);
      http.get.mockReturnValue(of({recipe,revealedUtc:'2026-10-10T18:30:00Z',ownPreferences:{avoidRedMeat:false,excludedIngredients:[]},partnerPreferences:{avoidRedMeat:false,excludedIngredients:[]}}));await f.componentInstance.shuffle();f.detectChanges();
      expect(auth.submit).toHaveBeenCalledExactlyOnceWith('cooking/contests/7/shuffle',{});expect(f.nativeElement.textContent).not.toContain('Milk');expect(f.componentInstance.shuffling()).toBe(true);
      await vi.advanceTimersByTimeAsync(8000);f.detectChanges();expect(f.nativeElement.textContent).toContain('Milk');await f.componentInstance.shuffle();expect(auth.submit).toHaveBeenCalledTimes(1);
    } finally {f.destroy();vi.useRealTimers();}
  });
  it('submits local time and IANA timezone then refreshes the shared state',async()=>{
    const auth={submit:vi.fn().mockResolvedValue({id:1})};
    const sharing={state:signal({connected:true,cookingContests:[]}),busy:signal(false),error:signal(''),load:vi.fn().mockResolvedValue(undefined)};
    TestBed.configureTestingModule({imports:[CookingContestsComponent],providers:[{provide:HttpClient,useValue:{get:vi.fn().mockReturnValue(of({recipe:null,ownPreferences:{avoidRedMeat:false,excludedIngredients:[]},partnerPreferences:{avoidRedMeat:false,excludedIngredients:[]}}))}},{provide:AuthService,useValue:auth},{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(CookingContestsComponent);const c=f.componentInstance;c.date='2026-10-10';c.hour='18';c.minute='30';c.availableMinutes='45';f.detectChanges();
    expect(f.nativeElement.querySelector('app-outing-calendar')).not.toBeNull();expect(f.nativeElement.querySelector('input[type=datetime-local]')).toBeNull();
    await c.invite();f.detectChanges();
    expect(auth.submit).toHaveBeenCalledExactlyOnceWith('cooking/contests',{localStart:'2026-10-10T18:30',timeZone:c.zone,availableMinutes:45});
    expect(sharing.load).toHaveBeenCalledWith(true);expect(c.notice()).toContain('Once your partner accepts');expect(c.busy()).toBe(false);f.destroy();
  });
});
