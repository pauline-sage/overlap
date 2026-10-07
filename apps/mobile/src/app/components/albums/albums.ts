import { Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { LoadingIndicatorComponent } from '../loading-indicator/loading-indicator';
import { DrawTogetherComponent } from '../draw-together/draw-together';
import { CookingMemoriesComponent } from '../cooking/memories/cooking-memories';
import { DrawingList } from '../draw-together/drawing-photo';

interface AlbumPhoto {key:string;category:'drawing'|'cooking';id:number;title:string;date:string;mine:boolean;url:string;}
interface CookingMemory {contestId:number;recipe:{title:string};photos:{accountId:string;revision:string;uploadedUtc:string;mine:boolean}[];}
@Component({selector:'app-albums',imports:[DatePipe,LoadingIndicatorComponent,DrawTogetherComponent,CookingMemoriesComponent],templateUrl:'./albums.html',styleUrl:'./albums.scss'})
export class AlbumsComponent {
  readonly back=output<void>();readonly ownName=input('You');readonly partnerName=input('Your person');
  readonly photos=signal<AlbumPhoto[]>([]);readonly loading=signal(true);readonly error=signal('');
  readonly category=signal<'all'|'cooking'|'drawing'>('all');readonly selected=signal<AlbumPhoto|null>(null);readonly page=signal(1);
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
        ...drawings.rounds.flatMap(r=>r.photos.map(p=>({key:'drawing-'+r.id+'-'+p.accountId+'-'+p.revision,category:'drawing' as const,id:r.id,title:r.prompt,date:p.uploadedUtc,mine:p.mine,url:'/api/auth/drawing/'+r.id+'/photos/'+encodeURIComponent(p.accountId)+'?v='+p.revision}))),
        ...cooking.flatMap(r=>r.photos.map(p=>({key:'cooking-'+r.contestId+'-'+p.accountId+'-'+p.revision,category:'cooking' as const,id:r.contestId,title:r.recipe.title,date:p.uploadedUtc,mine:p.mine,url:'/api/auth/cooking/memories/'+r.contestId+'/photos/'+encodeURIComponent(p.accountId)+'?v='+p.revision})))
      ];
      this.photos.set(photos.sort((a,b)=>Date.parse(b.date)-Date.parse(a.date)));this.page.set(1);
    }catch{if(!this.destroyed)this.error.set('Your albums could not be loaded. Try again.');}
    finally{this.reading=false;if(!this.destroyed)this.loading.set(false);}
  }
  filter(category:'all'|'cooking'|'drawing'):void{this.category.set(category);this.page.set(1);}
  visible():AlbumPhoto[]{return this.photos().filter(x=>this.category()==='all'||x.category===this.category());}
  close():void{this.selected.set(null);void this.load();}
}
