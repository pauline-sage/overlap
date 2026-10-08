import { NavigationStateService, navigationId, oneOf } from '../../../navigation-state.service';
import { HttpClient } from '@angular/common/http';
import { Component, inject, signal, output } from '@angular/core';
import { DatePipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../../auth.service';

interface ClosedConnection { pairId:string; partnerName?:string|null; disconnectedUtc:string|null; exportExpiresUtc:string|null; }
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
        } @empty {<div class="collection-empty"><span class="collection-empty-icon" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M4 5h16v14H4zM4 13h5l2 3h2l2-3h5"/></svg></span><h3>No saved records here</h3><p>Saved items from this connection will appear here.</p></div>}
        @if(page()>0){<button type="button" (click)="page.update(previous)">Previous</button>}
        @if((page()+1)*5 < items().length){<button type="button" (click)="page.update(next)">Next</button>}
      </section>
    } @else {
      <section aria-labelledby="connection-data-heading">
        @if(creator()) {
          <button class="archive-disclosure" type="button" (click)="expanded.set(!expanded())" [attr.aria-expanded]="expanded()" aria-controls="saved-connections"><span id="connection-data-heading">My archive <span class="count">{{connections().length}}</span></span><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><path [attr.d]="expanded() ? 'm6 15 6-6 6 6' : 'm6 9 6 6 6-6'"/></svg></button>
        } @else {
          <h3 id="connection-data-heading">Connection data</h3>
          <p>Ending a connection removes it from the app permanently. Download your data within 30 days. After that, shared records are deleted automatically, unless retained in a creator archive.</p>
        }
        @if(!creator() || expanded()) {
        <div id="saved-connections">
        @for(connection of connections();track connection.pairId){
          <article>
            <div class="archive-row"><div><h4>With {{connection.partnerName || 'your former partner'}}</h4><p>{{connection.disconnectedUtc ? 'Ended' : 'Previous connection'}} @if(connection.disconnectedUtc){ · {{connection.disconnectedUtc | date:'mediumDate'}}}</p></div>
            @if(creator()){<button class="delete-archive" type="button" (click)="removing.set(connection.pairId)" [disabled]="busy()" aria-label="Remove this archive" title="Remove archive"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 10v7M14 10v7"/></svg></button>}
            </div>
            @if(removing()===connection.pairId){
              <p>Remove this archive? Your access ends permanently. Shared records will be deleted after your partner's 30-day download window ends.</p>
              <button type="button" (click)="remove(connection)" [disabled]="busy()">Remove archive</button>
              <button type="button" (click)="removing.set('')" [disabled]="busy()">Cancel</button>
            } @else {
            @if(!creator()){<p>Download until {{connection.exportExpiresUtc | date:'medium'}}</p>}
            <button type="button" (click)="download(connection)" [disabled]="busy()">Download data</button>
            @if(creator()){<button type="button" (click)="open(connection)" [disabled]="busy()">Open my archive</button>}
            }
          </article>
        } @empty {<div class="collection-empty"><span class="collection-empty-icon" aria-hidden="true"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"><path d="M4 5h16v14H4zM4 13h5l2 3h2l2-3h5"/></svg></span><h3>No saved connections</h3><p>Your available connection archives will appear here.</p></div>}
        </div>
        }
      </section>
    }
    @if(busy()){<p role="status">{{downloading()?'Preparing download…':'Loading archive…'}}</p>}
    @if(error()){<p role="alert">{{error()}}</p>}
  `,
  styles:`:host{display:block;margin:1.5rem 0}h3,h4{color:var(--text)}p{color:var(--muted);line-height:1.6;font-size:.85rem}article{padding:1rem;margin:.75rem 0;border:1px solid var(--line);border-radius:1rem;background:var(--surface)}button{font:inherit;color:var(--text);border:1px solid var(--line);border-radius:.8rem;background:var(--surface);padding:.7rem 1rem;min-height:44px;margin:.2rem;cursor:pointer}button:disabled{opacity:.5}button[aria-pressed=true]{background:var(--accent-soft)}.categories{display:flex;flex-wrap:wrap;gap:.3rem}.photos{display:flex;gap:.5rem}figure{margin:0;min-width:0;flex:1}img{width:100%;border-radius:.7rem}figcaption{font-size:.8rem;color:var(--muted)}.archive-disclosure{display:flex;align-items:center;justify-content:space-between;width:100%;margin:0;padding:1rem;text-align:left;font-weight:600}.archive-disclosure svg,.delete-archive svg{width:20px;height:20px}.count{font-size:.8rem;color:var(--muted);margin-left:.4rem}.archive-row{display:flex;align-items:center;justify-content:space-between;gap:.5rem}.delete-archive{padding:.6rem;flex-shrink:0}.archive-row p{margin:0}.archive-row h4{margin:0 0 .3rem;font-size:.95rem}`
})
export class ConnectionDataComponent {
  readonly archiveOpened=output<boolean>();
  close():void{this.navigation.write("archive.pair","");this.archive.set(null);this.archiveOpened.emit(false);}
  private readonly http=inject(HttpClient);
  private readonly auth=inject(AuthService);
  readonly expanded=signal(false);
  readonly removing=signal('');
  private readonly navigation=inject(NavigationStateService);
  readonly connections=signal<ClosedConnection[]>([]);
  readonly creator=signal(false);readonly busy=signal(false);readonly downloading=signal(false);readonly error=signal('');
  readonly archive=signal<Record<string,ArchiveItem[]>|null>(null);readonly category=this.navigation.remember('archive.category',oneOf(['drawings','cooking','books','recipes','watch','watched','outings','places','dates','quizzes','customQuizzes']),'drawings');readonly page=this.navigation.remember('archive.page',navigationId,0);
  private pair='';
  readonly categories=[{key:'drawings',label:'Drawing'},{key:'cooking',label:'Cooking'},{key:'books',label:'Books'},{key:'recipes',label:'Recipes'},{key:'watch',label:'Watch picks'},{key:'watched',label:'Seen together'},{key:'outings',label:'Outings'},{key:'places',label:'Places'},{key:'dates',label:'Dates'},{key:'quizzes',label:'Quizzes'},{key:'customQuizzes',label:'My quizzes'}] as const;
  readonly previous=(n:number)=>n-1;readonly next=(n:number)=>n+1;
  constructor(){void this.load();}
  async remove(connection:ClosedConnection):Promise<void>{
    if(this.busy() || !this.creator() || this.removing()!==connection.pairId)return;
    this.busy.set(true);this.error.set('');
    try{await this.auth.submit(`connection-history/${connection.pairId}/remove`,{confirm:true});this.connections.update(rows=>rows.filter(row=>row.pairId!==connection.pairId));this.removing.set('');if(this.pair===connection.pairId)this.close();}
    catch{this.error.set('This archive could not be removed. Try again.');}
    finally{this.busy.set(false);}
  }
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
