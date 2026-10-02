import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of, Subject } from 'rxjs';
import { AuthService } from '../../auth.service';
import { CookingComponent } from './cooking';
const recipe={id:12,revision:34,title:'Flan',ingredients:['Milk'],instructions:['Mix'],sourceUrl:'https://en.wikibooks.org/w/index.php?oldid=34',photo:{url:'https://upload.wikimedia.org/example.jpg',author:'An author',license:'CC BY-SA 4.0',licenseUrl:'https://creativecommons.org/licenses/by-sa/4.0/',sourceUrl:'https://commons.wikimedia.org/wiki/File:Flan.jpg'}};
describe('Cooking',()=>{
 const auth={submit:vi.fn()}; const http={get:vi.fn()};
 beforeEach(()=>{vi.resetAllMocks();http.get.mockReturnValue(of({connected:true,entries:[]}));TestBed.configureTestingModule({imports:[CookingComponent],providers:[{provide:AuthService,useValue:auth},{provide:HttpClient,useValue:http}]});});
 async function render(){const f=TestBed.createComponent(CookingComponent);f.detectChanges();await f.whenStable();return f;}
 it('moves between matching tabs with keyboard navigation',async()=>{
  const f=await render();const tabs=f.nativeElement.querySelectorAll('[role="tab"]');
  expect(tabs.length).toBe(4);expect(Array.from(tabs as NodeListOf<HTMLElement>).map(x=>x.textContent?.trim())).toEqual(['Discover', "Me's", "Your person's", 'Our recipes']);expect(tabs[0].getAttribute('aria-selected')).toBe('true');
  tabs[0].dispatchEvent(new KeyboardEvent('keydown',{key:'End',bubbles:true,cancelable:true}));await f.whenStable();f.detectChanges();
  expect(f.componentInstance.tab()).toBe('collection');expect(document.activeElement).toBe(tabs[3]);
  expect(f.nativeElement.querySelector('[role="tabpanel"]').getAttribute('aria-labelledby')).toBe(tabs[3].id);
  tabs[3].dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true,cancelable:true}));await f.whenStable();f.detectChanges();
  expect(f.componentInstance.tab()).toBe('discover');expect(tabs[0].tabIndex).toBe(0);expect(tabs[3].tabIndex).toBe(-1);f.destroy();
 });
 it('submits from the keyboard without saving a recipe or sharing a search',async()=>{
  auth.submit.mockResolvedValue([{id:12,title:'Flan'}]);const f=await render();const c=f.componentInstance;c.query='flan';f.detectChanges();
  const field=f.nativeElement.querySelector('input');field.focus();field.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true,cancelable:true}));await f.whenStable();
  expect(document.activeElement).not.toBe(field);expect(auth.submit).toHaveBeenCalledExactlyOnceWith('cooking/search',{query:'flan'});f.destroy();
 });
 it('shows an external recipe picture and saves only after an explicit action',async()=>{
  auth.submit.mockResolvedValue({recipe,token:'protected-preview'});const f=await render();await f.componentInstance.open({id:12,title:'Flan'});f.detectChanges();
  expect(f.nativeElement.querySelector('img').getAttribute('src')).toBe(recipe.photo.url);expect(f.nativeElement.textContent).toContain('Milk');expect(f.nativeElement.textContent).toContain('Wikibooks contributors');
  expect(auth.submit).toHaveBeenCalledTimes(1);http.get.mockReturnValue(of({connected:true,entries:[{id:1,recipe}]}));auth.submit.mockResolvedValue({});await f.componentInstance.save();
  expect(auth.submit).toHaveBeenLastCalledWith('cooking/collection',{token:'protected-preview'});expect(f.componentInstance.saved()).toBe(true);f.destroy();
 });
 it('opens a saved snapshot without a provider call',async()=>{
  http.get.mockReturnValue(of({connected:true,entries:[{id:1,recipe,addedUtc:'2026-10-02T08:00:00Z'}]}));const f=await render();f.componentInstance.openSaved(f.componentInstance.entries()[0]);f.detectChanges();
  expect(auth.submit).not.toHaveBeenCalled();expect(f.nativeElement.textContent).toContain('In your shared collection');f.destroy();
 });
 it('keeps ingredient and step checks local and resets them for another recipe',async()=>{
  http.get.mockReturnValue(of({connected:true,entries:[{id:1,recipe,addedUtc:'2026-10-02T08:00:00Z'}]}));const f=await render();const c=f.componentInstance;c.openSaved(c.entries()[0]);f.detectChanges();
  const checks=f.nativeElement.querySelectorAll('.check-row input');checks[0].click();checks[1].click();f.detectChanges();
  expect(c.ingredientChecks()).toEqual([0]);expect(c.stepChecks()).toEqual([0]);expect(auth.submit).not.toHaveBeenCalled();
  c.openSaved(c.entries()[0]);expect(c.ingredientChecks()).toEqual([]);expect(c.stepChecks()).toEqual([]);f.destroy();
 });
 it('shares an existing recipe and records an independent made rating',async()=>{
  http.get.mockReturnValue(of({connected:true,entries:[{id:1,recipe,addedUtc:'2026-10-02T08:00:00Z'}]}));auth.submit.mockResolvedValue({});const f=await render();const c=f.componentInstance;c.openSaved(c.entries()[0]);
  await c.share();expect(auth.submit).toHaveBeenLastCalledWith('cooking/collection/1/share',{});expect(c.notice()).toContain('Notifications');
  await c.experience(true,4);expect(auth.submit).toHaveBeenLastCalledWith('cooking/collection/1/experience',{made:true,stars:4});expect(c.entry()?.ownRating).toBe(4);f.destroy();
 });
 it('separates my made recipes and partner history, respecting the partner sharing flag',async()=>{
  http.get.mockReturnValue(of({connected:true,partnerCookingShared:false,entries:[{id:1,recipe,ownMade:true,ownRating:4},{id:2,recipe:{...recipe,id:13,title:'Pasta'},ownMade:false,partnerMade:false}]}));
  const f=await render();const c=f.componentInstance;c.view('made');await f.whenStable();f.detectChanges();expect(c.visibleEntries().map(x=>x.id)).toEqual([1]);expect(f.nativeElement.textContent).toContain('4/5');
  c.view('partner');await f.whenStable();f.detectChanges();expect(c.visibleEntries()).toEqual([]);expect(f.nativeElement.textContent).toContain('history is private');
  http.get.mockReturnValue(of({connected:true,partnerCookingShared:true,entries:[{id:2,recipe:{...recipe,id:13,title:'Pasta'},ownMade:false,partnerMade:true,partnerRating:5}]}));await c.load();f.detectChanges();expect(c.visibleEntries().map(x=>x.id)).toEqual([2]);expect(f.nativeElement.textContent).toContain('5/5');f.destroy();
 });
 it('saves an unsaved recipe before marking it made and rejects a stale polling response',async()=>{
  const f=await render();const c=f.componentInstance;
  const stale=new Subject<{connected:boolean;entries:unknown[]}>();http.get.mockReturnValueOnce(stale);void c.load();
  auth.submit.mockResolvedValueOnce({recipe,token:'fresh-token'});await c.open({id:12,title:'Flan'});
  http.get.mockReturnValue(of({connected:true,entries:[{id:1,recipe,ownMade:false}]}));auth.submit.mockResolvedValue({});
  await c.experience(true);
  expect(auth.submit).toHaveBeenCalledWith('cooking/collection',{token:'fresh-token'});
  expect(auth.submit).toHaveBeenLastCalledWith('cooking/collection/1/experience',{made:true,stars:null});
  stale.next({connected:true,entries:[]});stale.complete();await f.whenStable();
  expect(c.entry()?.ownMade).toBe(true);expect(c.error()).toBe('');c.view('made');expect(c.visibleEntries().length).toBe(1);f.destroy();
 });
});
