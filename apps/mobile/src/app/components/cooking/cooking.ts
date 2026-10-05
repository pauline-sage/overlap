import { CookingMemoriesComponent } from './cooking-memories';
import { Component, inject, OnInit, OnDestroy, input, output, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../auth.service';
import { FoodPreferencesComponent } from './food-preferences';
import { CookingContestsComponent } from './cooking-contests';
import { BookRatingComponent } from '../read-together/book-rating';
import { LoadingIndicatorComponent } from '../loading-indicator';
interface RecipeHit { id: number; title: string; }
interface Recipe extends RecipeHit { revision: number; ingredients: string[]; instructions: string[]; sourceUrl: string; photo: { url: string; author: string; license: string; licenseUrl: string; sourceUrl: string } | null; }
interface Entry { ownMadeUtc?:string|null; partnerMadeUtc?:string|null; ownMade?: boolean; partnerMade?: boolean; ownRating?: number | null; partnerRating?: number | null; id: number; addedUtc: string; recipe: Recipe; }
@Component({ selector: 'app-cooking', imports: [CookingMemoriesComponent, DatePipe, FormsModule, LoadingIndicatorComponent, BookRatingComponent, CookingContestsComponent, FoodPreferencesComponent], templateUrl: './cooking.html', styleUrl: './cooking.scss' })
export class CookingComponent implements OnInit, OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly http = inject(HttpClient);
  readonly back = output<void>();
  readonly initialRecipeId=input(0);
  readonly initialContestId=input(0);
  readonly partnerName=input('Your person');
  readonly ownName=input('Me');
  readonly partnerCookingShared=signal(false);
  private initialOpened=false;
  readonly preferencesOpened=signal(false);
  readonly contestsView=viewChild(CookingContestsComponent);
  refreshFoodPreferences():void {const view=this.contestsView();if(view?.selectedId())void view.loadRecipe(view.selectedId());}
  closeFoodPreferences():void {this.preferencesOpened.set(false);this.refreshFoodPreferences();}
  readonly albumOpened=signal(false);
  readonly contestsOpened=signal(false);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly collectionError = signal('');
  readonly tab = signal<'discover' | 'collection' | 'made' | 'partner'>('discover');
  readonly hits = signal<RecipeHit[] | null>(null);
  readonly entries = signal<Entry[]>([]);
  readonly connected = signal(false);
  readonly selected = signal<Recipe | null>(null);
  readonly failedPhoto = signal(false);
  readonly ingredientChecks = signal<number[]>([]);
  readonly stepChecks = signal<number[]>([]);
  readonly notice = signal('');
  query = '';
  private token: string | null = null;
  private poll?: ReturnType<typeof setInterval>;
  private disposed = false;
  private loading = false;
  private collectionVersion=0;
  ngOnInit(): void { this.contestsOpened.set(this.initialContestId()>0);void this.load(); this.poll=setInterval(() => { if(document.visibilityState==='visible') void this.load(); },10000); }
  ngOnDestroy(): void { this.disposed=true; clearInterval(this.poll); }
  view(tab: 'discover' | 'collection' | 'made' | 'partner'): void { this.tab.set(tab); this.selected.set(null); this.error.set(''); void this.load(); }
  tabKey(event:KeyboardEvent):void {
    const tabs=['discover','made','partner','collection'] as const;
    let index=tabs.indexOf(this.tab());
    if(event.key==='ArrowRight')index=(index+1)%tabs.length;
    else if(event.key==='ArrowLeft')index=(index+tabs.length-1)%tabs.length;
    else if(event.key==='Home')index=0;
    else if(event.key==='End')index=tabs.length-1;
    else return;
    event.preventDefault();this.view(tabs[index]);document.getElementById('cooking-tab-'+tabs[index])?.focus();
  }
  async load(force=false): Promise<void> {
    if((this.loading || this.busy()) && !force) return;
    this.loading=true;
    const version=++this.collectionVersion;
    try { const list=await firstValueFrom(this.http.get<{connected:boolean;partnerCookingShared?:boolean;entries:Entry[]}>('/api/auth/cooking/collection')); if(!this.disposed && version===this.collectionVersion) { this.entries.set(list.entries); this.connected.set(list.connected);this.partnerCookingShared.set(list.partnerCookingShared ?? false); this.collectionError.set(''); if(!this.initialOpened && this.initialRecipeId()) { this.initialOpened=true; const entry=list.entries.find(x=>x.id===this.initialRecipeId()); if(entry) this.openSaved(entry); else this.collectionError.set('This recipe was removed from your collection.'); } } }
    catch { if(!this.disposed && version===this.collectionVersion) this.collectionError.set('Your recipe collection could not be loaded. Try again.'); }
    finally { this.loading=false; }
  }
  dismissSearchKeyboard():void {
    document.getElementById('recipe-query')?.blur();
    if(document.activeElement instanceof HTMLElement) document.activeElement.blur();
  }
  async search(): Promise<void> {
    this.dismissSearchKeyboard();
    if(this.busy() || !this.query.trim()) return;
    this.busy.set(true); this.error.set(''); this.hits.set(null);
    try { const hits=await this.auth.submit<RecipeHit[]>('cooking/search',{query:this.query}); if(!this.disposed) this.hits.set(hits); }
    catch(e) { this.problem(e); } finally { this.busy.set(false); }
  }
  async open(hit: RecipeHit): Promise<void> {
    if(this.busy()) return;
    this.busy.set(true); this.error.set(''); this.failedPhoto.set(false); this.resetProgress();
    try { const detail=await this.auth.submit<{recipe:Recipe;token:string|null}>('cooking/details',{id:hit.id}); if(!this.disposed) { this.selected.set(detail.recipe); this.token=detail.token; } }
    catch(e) { this.problem(e); } finally { this.busy.set(false); }
  }
  openSaved(entry: Entry): void { this.selected.set(entry.recipe); this.token=null; this.failedPhoto.set(false); this.resetProgress(); this.error.set(''); }
  visibleEntries():Entry[] { return this.entries().filter(x=>this.tab()==='made'?x.ownMade:this.tab()==='partner'?this.partnerCookingShared() && x.partnerMade:true); }
  resetProgress(): void { this.ingredientChecks.set([]); this.stepChecks.set([]); this.notice.set(''); }
  toggleCheck(kind:'ingredients'|'steps',index:number):void { const state=kind==='ingredients'?this.ingredientChecks:this.stepChecks;state.update(items=>items.includes(index)?items.filter(x=>x!==index):[...items,index]); }
  entry(): Entry | undefined { return this.entries().find(x=>x.recipe.id===this.selected()?.id); }
  async ensureSaved():Promise<Entry|undefined> {
    if(this.entry())return this.entry();
    if(!this.token)throw new Error('Open the recipe again before saving.');
    await this.auth.submit('cooking/collection',{token:this.token});
    ++this.collectionVersion;
    const list=await firstValueFrom(this.http.get<{connected:boolean;partnerCookingShared?:boolean;entries:Entry[]}>('/api/auth/cooking/collection'));this.entries.set(list.entries);this.connected.set(list.connected);this.partnerCookingShared.set(list.partnerCookingShared ?? false);
    return list.entries.find(x=>x.recipe.id===this.selected()?.id);
  }
  async share():Promise<void> {
    if(this.busy())return;++this.collectionVersion;this.busy.set(true);this.error.set('');
    try {const entry=await this.ensureSaved();if(!entry)throw new Error();await this.auth.submit(`cooking/collection/${entry.id}/share`,{});this.notice.set('Recipe shared. Your person will see it in Notifications.');}
    catch(e){this.problem(e);}finally{this.busy.set(false);}
  }
  async experience(made:boolean,stars:number|null=null):Promise<void> {
    if(this.busy())return;++this.collectionVersion;this.busy.set(true);this.error.set('');
    try {const entry=await this.ensureSaved();if(!entry)throw new Error();const result=await this.auth.submit<{madeUtc?:string}>(`cooking/collection/${entry.id}/experience`,{made,stars});
      this.entries.update(items=>items.map(x=>x.id===entry.id?{...x,ownMade:made,ownRating:stars,ownMadeUtc:made ? result?.madeUtc ?? x.ownMadeUtc : null}:x));}
    catch(e){this.problem(e);}finally{this.busy.set(false);}
  }
  saved(): boolean { return this.entries().some(x => x.recipe.id===this.selected()?.id); }
  async save(): Promise<void> {
    if(this.busy() || !this.token) return;
    this.busy.set(true); this.error.set('');
    try { ++this.collectionVersion; await this.ensureSaved(); }
    catch(e) { this.problem(e); } finally { this.busy.set(false); }
  }
  async remove(entry: Entry): Promise<void> {
    if(this.busy()) return;
    this.busy.set(true); this.error.set('');
    try { await this.auth.submit(`cooking/collection/${entry.id}/remove`,{}); await this.load(true); }
    catch(e) { this.problem(e); } finally { this.busy.set(false); }
  }
  close(): void { this.selected.set(null); this.error.set(''); }
  private problem(error: unknown): void { if(!this.disposed) this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Cooking is unavailable. Please try again.' : 'Please try again.'); }
}
