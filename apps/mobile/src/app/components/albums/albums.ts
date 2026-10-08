import { NavigationStateService, oneOf, navigationId } from '../../navigation-state.service';
import { Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { LoadingIndicatorComponent } from '../loading-indicator/loading-indicator';
import { DrawTogetherComponent } from '../draw-together/draw-together';
import { CookingMemoriesComponent } from '../cooking/memories/cooking-memories';
import { DrawingList } from '../draw-together/drawing-photo';

interface AlbumPicture {key:string;mine:boolean;url:string;}
interface AlbumPhoto {partnerLabel?:string;pictures?:AlbumPicture[];key:string;category:'drawing'|'cooking';id:number;title:string;date:string;mine:boolean;url:string;}
interface CookingMemory {currentConnection?:boolean;contestId:number;recipe:{title:string};photos:{accountId:string;revision:string;uploadedUtc:string;mine:boolean}[];}
@Component({selector:'app-albums',imports:[DatePipe,LoadingIndicatorComponent,DrawTogetherComponent,CookingMemoriesComponent],templateUrl:'./albums.html',styleUrl:'./albums.scss'})
export class AlbumsComponent {
  private readonly navigation=inject(NavigationStateService);
  readonly back=output<void>();readonly ownName=input('You');readonly partnerName=input('Your person');
  readonly photos=signal<AlbumPhoto[]>([]);readonly loading=signal(true);readonly error=signal('');
  readonly category=this.navigation.remember<'all'|'cooking'|'drawing'>('albums.category',oneOf(['all','cooking','drawing']),'all');readonly selected=signal<AlbumPhoto|null>(null);readonly page=this.navigation.remember('albums.page',(value):value is number=>navigationId(value) && value>0,1);
  readonly detailId=this.navigation.remember('albums.detailId',navigationId,0);
  readonly detailCategory=this.navigation.remember<'drawing'|'cooking'>('albums.detailCategory',oneOf(['drawing','cooking']),'drawing');
  readonly failed=signal<string[]>([]);private readonly http=inject(HttpClient);private destroyed=false;private reading=false;
  constructor(){inject(DestroyRef).onDestroy(()=>this.destroyed=true);effect(()=>{void this.load();});}
  async load():Promise<void>{
    if(this.reading)return;this.reading=true;this.loading.set(true);this.error.set('');
    try{
      const [drawings,cooking]=await Promise.all([
        firstValueFrom(this.http.get<DrawingList>('/api/auth/drawing')),
        firstValueFrom(this.http.get<CookingMemory[]>('/api/auth/cooking/memories'))
      ]);
      if(this.destroyed)return;
      const photos:AlbumPhoto[]=[
        ...drawings.rounds.filter(r=>r.photos.length>0).map(r=>{
          const pictures=r.photos.map(p=>({key:'drawing-'+r.id+'-'+p.accountId+'-'+p.revision,mine:p.mine,url:'/api/auth/drawing/'+r.id+'/photos/'+encodeURIComponent(p.accountId)+'?v='+p.revision}));
          const date=r.startUtc || r.createdUtc || r.photos.map(p=>p.uploadedUtc).sort((a,b)=>Date.parse(a)-Date.parse(b))[0];
          return {partnerLabel:r.currentConnection===false ? 'Partner' : undefined,key:'drawing-'+r.id,category:'drawing' as const,id:r.id,title:r.prompt,date,mine:pictures[0].mine,url:pictures[0].url,pictures};
        }),
        ...cooking.flatMap(r=>r.photos.map(p=>({partnerLabel:r.currentConnection===false ? 'Partner' : undefined,key:'cooking-'+r.contestId+'-'+p.accountId+'-'+p.revision,category:'cooking' as const,id:r.contestId,title:r.recipe.title,date:p.uploadedUtc,mine:p.mine,url:'/api/auth/cooking/memories/'+r.contestId+'/photos/'+encodeURIComponent(p.accountId)+'?v='+p.revision})))
      ];
      this.photos.set(photos.sort((a,b)=>Date.parse(b.date)-Date.parse(a.date)));this.page.set(Math.min(this.page(),Math.max(1,Math.ceil(this.visible().length/12))));
      const selected=photos.find(p=>p.id===this.detailId() && p.category===this.detailCategory());
      this.selected.set(selected ?? null);if(!selected)this.detailId.set(0);
    }catch{if(!this.destroyed)this.error.set('Your albums could not be loaded. Try again.');}
    finally{this.reading=false;if(!this.destroyed)this.loading.set(false);}
  }
  picture(entry:AlbumPhoto,mine:boolean):AlbumPicture|undefined{return entry.pictures?.find(p=>p.mine===mine);}
  filter(category:'all'|'cooking'|'drawing'):void{this.category.set(category);this.page.set(1);}
  visible():AlbumPhoto[]{return this.photos().filter(x=>this.category()==='all'||x.category===this.category());}
  open(photo:AlbumPhoto):void{this.detailCategory.set(photo.category);this.detailId.set(photo.id);this.selected.set(photo);}
  close():void{this.detailId.set(0);this.selected.set(null);void this.load();}
}
