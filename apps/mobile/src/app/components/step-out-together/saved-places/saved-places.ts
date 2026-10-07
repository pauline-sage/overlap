import { CollectionBrowser, CollectionBrowserComponent } from '../../collection-browser/collection-browser';
import { Component, computed, inject, input, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../../auth.service';
import { LoadingIndicatorComponent } from '../../loading-indicator/loading-indicator';
interface Identity {name:string;description:string;sourceUrl:string;photoUrl:string|null;photoSourceUrl:string|null;credit:string|null;license:string|null;licenseUrl:string|null;}
interface Match {place:Identity;token:string;}
interface Place {identity?:Identity|null;id:number;name:string;location:string;link:string|null;note:string|null;revision:string;mine:boolean;}
@Component({selector:'app-saved-places',imports:[FormsModule,LoadingIndicatorComponent,CollectionBrowserComponent],templateUrl:'./saved-places.html',styleUrl:'./saved-places.scss'})
export class SavedPlacesComponent implements OnInit {
  readonly searchPages=new CollectionBrowser();
  readonly partnerName=input('Your person');
  private readonly http=inject(HttpClient);private readonly auth=inject(AuthService);readonly back=output<void>();
  readonly places=signal<Place[]>([]);readonly connected=signal(false);readonly busy=signal(false);readonly error=signal('');readonly notice=signal('');
  readonly editing=signal<Place|null>(null);readonly formOpen=signal(false);readonly removing=signal<number|null>(null);readonly filter=signal('');readonly page=signal(0);
  name='';location='';link='';note='';query='';language='en';
  readonly matches=signal<Match[]>([]);readonly searching=signal(false);readonly searched=signal(false);readonly selected=signal<Identity|null>(null);private selectionToken:string|undefined;private clearIdentity=false;
  readonly brokenImages=signal<string[]>([]);
  imageFailed(url:string):void{this.brokenImages.update(items=>[...items,url]);}
  async find():Promise<void>{if(this.searching()||!this.query.trim())return;(document.activeElement as HTMLElement|null)?.blur();this.searchPages.reset(); this.searching.set(true);this.error.set('');this.matches.set([]);try{this.matches.set(await firstValueFrom(this.http.get<Match[]>('/api/auth/saved-places/search',{params:{query:this.query,language:this.language}})));this.searched.set(true);}catch(e){this.fail(e);}finally{this.searching.set(false);}}
  select(match:Match):void{this.selected.set(match.place);this.selectionToken=match.token;this.clearIdentity=false;this.name=match.place.name;this.matches.set([]);}
  detach():void{this.selected.set(null);this.selectionToken=undefined;this.clearIdentity=true;}

  readonly filtered=computed(()=>{const q=this.filter().trim().toLocaleLowerCase();return this.places().filter(p=>(p.name+' '+p.location+' '+(p.note||'')).toLocaleLowerCase().includes(q));});
  readonly visible=computed(()=>this.filtered().slice(this.page()*5,(this.page()+1)*5));
  readonly pages=computed(()=>Math.max(1,Math.ceil(this.filtered().length/5)));
  async ngOnInit():Promise<void>{await this.load();}
  async load():Promise<void>{this.busy.set(true);this.error.set('');try{const data=await firstValueFrom(this.http.get<{connected:boolean;items:Place[]}>('/api/auth/saved-places'));this.connected.set(data.connected);this.places.set(data.items);this.page.set(Math.min(this.page(),this.pages()-1));}catch(e){this.fail(e);}finally{this.busy.set(false);}}
  open(place:Place|null=null):void{this.editing.set(place);this.name=place?.name||'';this.location=place?.location||'';this.link=place?.link||'';this.note=place?.note||'';this.formOpen.set(true);this.error.set('');this.notice.set('');this.removing.set(null);this.selected.set(place?.identity||null);this.selectionToken=undefined;this.clearIdentity=false;this.query=place?place.name+' '+place.location:'';this.matches.set([]);this.searched.set(false);}
  async save():Promise<void>{if(this.busy())return;(document.activeElement as HTMLElement|null)?.blur();this.busy.set(true);this.error.set('');try{const place=this.editing();await this.auth.submit('saved-places'+(place?'/'+place.id:''),{name:this.name,location:this.location,link:this.link,note:this.note,revision:place?.revision,selectionToken:this.selectionToken,clearIdentity:this.clearIdentity});this.formOpen.set(false);await this.load();this.notice.set('Place saved for both of you.');}catch(e){this.fail(e);}finally{this.busy.set(false);}}
  async remove(place:Place):Promise<void>{if(this.busy()||this.removing()!==place.id)return;this.busy.set(true);this.error.set('');try{await this.auth.submit('saved-places/'+place.id+'/remove',{revision:place.revision});this.removing.set(null);await this.load();this.notice.set('Place removed from your shared list.');}catch(e){this.fail(e);}finally{this.busy.set(false);}}
  search(value:string):void{this.filter.set(value);this.page.set(0);}
  private fail(e:unknown):void{this.error.set(e instanceof HttpErrorResponse ? e.error?.title || 'Could not update your places. Try again.' : 'Could not update your places. Try again.');}
}
