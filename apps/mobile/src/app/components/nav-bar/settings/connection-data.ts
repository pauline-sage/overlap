import { NavigationStateService, navigationId, oneOf } from '../../../navigation-state.service';
import { HttpClient } from '@angular/common/http';
import { Component, inject, signal, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';

interface ClosedConnection { pairId:string; disconnectedUtc:string|null; exportExpiresUtc:string|null; }
interface ArchivePhoto { accountId:string; mine:boolean; }
type ArchiveItem = Record<string, unknown> & { photos?:ArchivePhoto[] };
@Component({
  selector:'app-connection-data',imports:[DatePipe],
  template:`
    @if(archive()) {
      <section aria-labelledby="archive-heading">
        <button type="button" (click)="close()">‹ Back</button>
        <h3 id="archive-heading">My archive</h3>
        <div class="categories" aria-label="Archive categories">
          @for(tab of categories;track tab.key){<button type="button" [attr.aria-pressed]="category()===tab.key" (click)="category.set(tab.key);page.set(0)">{{tab.label}}</button>}
        </div>
        @for(item of visible();track $index){
          <article>
            <h4>{{title(item)}}</h4>
            @if(date(item)){<p>{{date(item) | date:'mediumDate'}}</p>}
            @for(line of details(item);track $index){<p>{{line}}</p>}
            <div class="photos">@for(photo of item.photos || [];track photo.accountId){<figure><img [src]="photoUrl(item,photo)" alt="Saved activity photo" /><figcaption>{{photo.mine?'You':'Your partner'}}</figcaption></figure>}</div>
          </article>
        } @empty {<p>No saved records here.</p>}
        @if(page()>0){<button type="button" (click)="page.update(previous)">Previous</button>}
        @if((page()+1)*5 < items().length){<button type="button" (click)="page.update(next)">Next</button>}
      </section>
    } @else {
      <section aria-labelledby="connection-data-heading">
        <h3 id="connection-data-heading">Connection data</h3>
        <p>Ending a connection removes its activities and shared records from the app. It cannot be restored. Downloads remain available for 30 days; stored records are retained.</p>
        @for(connection of connections();track connection.pairId){
          <article>
            <p>{{connection.disconnectedUtc ? 'Connection ended' : 'Previous connection'}} @if(connection.disconnectedUtc){ · {{connection.disconnectedUtc | date:'mediumDate'}}}</p>
            @if(!creator()){<p>Download until {{connection.exportExpiresUtc | date:'medium'}}</p>}
            <button type="button" (click)="download(connection)" [disabled]="busy()">Download data</button>
            @if(creator()){<button type="button" (click)="open(connection)" [disabled]="busy()">Open my archive</button>}
          </article>
        }
      </section>
    }
    @if(busy()){<p role="status">{{downloading()?'Preparing download…':'Loading archive…'}}</p>}
    @if(error()){<p role="alert">{{error()}}</p>}
  `,
  styles:`:host{display:block;margin:1.5rem 0}h3,h4{color:var(--text)}p{color:var(--muted);line-height:1.6;font-size:.85rem}article{padding:1rem;margin:.75rem 0;border:1px solid var(--line);border-radius:1rem;background:var(--surface)}button{font:inherit;color:var(--text);border:1px solid var(--line);border-radius:.8rem;background:var(--surface);padding:.7rem 1rem;min-height:44px;margin:.2rem;cursor:pointer}button:disabled{opacity:.5}button[aria-pressed=true]{background:var(--accent-soft)}.categories{display:flex;flex-wrap:wrap;gap:.3rem}.photos{display:flex;gap:.5rem}figure{margin:0;min-width:0;flex:1}img{width:100%;border-radius:.7rem}figcaption{font-size:.8rem;color:var(--muted)}`
})
export class ConnectionDataComponent {
  readonly archiveOpened=output<boolean>();
  close():void{this.navigation.write("archive.pair","");this.archive.set(null);this.archiveOpened.emit(false);}
  private readonly http=inject(HttpClient);
  private readonly navigation=inject(NavigationStateService);
  readonly connections=signal<ClosedConnection[]>([]);
  readonly creator=signal(false);readonly busy=signal(false);readonly downloading=signal(false);readonly error=signal('');
  readonly archive=signal<Record<string,ArchiveItem[]>|null>(null);readonly category=this.navigation.remember('archive.category',oneOf(['drawings','cooking','books','recipes','watch','watched','outings','places','dates','quizzes','customQuizzes']),'drawings');readonly page=this.navigation.remember('archive.page',navigationId,0);
  private pair='';
  readonly categories=[{key:'drawings',label:'Drawing'},{key:'cooking',label:'Cooking'},{key:'books',label:'Books'},{key:'recipes',label:'Recipes'},{key:'watch',label:'Watch picks'},{key:'watched',label:'Seen together'},{key:'outings',label:'Outings'},{key:'places',label:'Places'},{key:'dates',label:'Dates'},{key:'quizzes',label:'Quizzes'},{key:'customQuizzes',label:'My quizzes'}] as const;
  readonly previous=(n:number)=>n-1;readonly next=(n:number)=>n+1;
  constructor(){void this.load();}
  private async load():Promise<void>{try{const result=await firstValueFrom(this.http.get<{creator:boolean;connections:ClosedConnection[]}>('/api/auth/connection-history'));this.creator.set(result.creator);this.connections.set(result.connections);const saved=this.navigation.read("archive.pair",(value):value is string=>typeof value==="string" && /^[a-f0-9]{32}$/.test(value),"");const restore=result.connections.find(c=>c.pairId===saved);if(result.creator && restore)await this.open(restore,true);}catch{this.error.set('Connection downloads could not be loaded. Try refreshing.');}}
  async open(connection:ClosedConnection,restore=false):Promise<void>{this.busy.set(true);this.error.set('');try{this.archive.set(await firstValueFrom(this.http.get<Record<string,ArchiveItem[]>>(`/api/auth/connection-history/${connection.pairId}/archive`)));this.pair=connection.pairId;this.navigation.write("archive.pair",this.pair);if(!restore)this.page.set(0);this.archiveOpened.emit(true);}catch{this.error.set('This archive is unavailable.');}finally{this.busy.set(false);}}
  async download(connection:ClosedConnection):Promise<void>{this.busy.set(true);this.downloading.set(true);this.error.set('');try{const blob=await firstValueFrom(this.http.get(`/api/auth/connection-history/${connection.pairId}/export`,{responseType:'blob'}));const url=URL.createObjectURL(blob);const link=document.createElement('a');link.href=url;link.download='overlap-connection.zip';link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);}catch{this.error.set('The download is unavailable or its 30-day window has ended.');}finally{this.busy.set(false);this.downloading.set(false);}}
  items():ArchiveItem[]{return [...(this.archive()?.[this.category()]||[])].sort((a,b)=>(this.date(b)||"").localeCompare(this.date(a)||""));}
  visible():ArchiveItem[]{return this.items().slice(this.page()*5,(this.page()+1)*5);}
  title(item:ArchiveItem):string{const nested=item['book']||item['recipe']||item['venue']||item['title'];const row=typeof nested==='object' && nested!==null ? nested as Record<string,unknown> : item;return String(row['title']||row['Title']||row['name']||row['Name']||item['title']||this.categories.find(c=>c.key===this.category())?.label||'Activity');}
  date(item:ArchiveItem):string|null{return (item['startUtc']||item['createdUtc']||item['addedUtc']||item['updatedUtc']||item['localStart']) as string|null || null;}
  photoUrl(item:ArchiveItem,photo:ArchivePhoto):string{return `/api/auth/connection-history/${this.pair}/photo/${this.category()==='drawings'?'drawing':'cooking'}/${item['id']}/${encodeURIComponent(photo.accountId)}`;}
  details(item:ArchiveItem):string[]{
    const lines:string[]=[];for(const key of ['status','note','location','cancellationReason'])if(item[key])lines.push(String(item[key]));
    const nested=item['book']||item['recipe']||item['title']||item['venue'];if(typeof nested==='object' && nested!==null){const data=nested as Record<string,unknown>;for(const key of ['author','authors','description','address','summary']){const value=data[key]??data[key.charAt(0).toUpperCase()+key.slice(1)];if(value)lines.push(Array.isArray(value)?value.join(', '):String(value));}}
    if(Array.isArray(item['questions']))for(const question of item['questions']){const q=question as Record<string,unknown>;lines.push(String(q['prompt']||''));const choices=q['choices'] as string[]|undefined;const reveal=q['reveal'] as Record<string,number|null>|null;if(reveal && choices){if(reveal['ownAnswer']!=null)lines.push('You: '+choices[reveal['ownAnswer']]);if(reveal['partnerAnswer']!=null)lines.push('Your partner: '+choices[reveal['partnerAnswer']]);}}
    return lines;
  }
}
