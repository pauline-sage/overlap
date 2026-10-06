import { Component, DestroyRef, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { DatePipe } from '@angular/common';
import { firstValueFrom } from 'rxjs';
import { SharingService } from '../../nav-bar/sharing/sharing.service';
import { CollectionBrowser, CollectionBrowserComponent } from '../../collection-browser/collection-browser';
import { LoadingIndicatorComponent } from '../../loading-indicator/loading-indicator';
import type { WatchTitle } from '../../shared-time/shared-time';
import type { BookTitle } from '../../read-together/read-together';

interface SharedItem {
  id: string; title: string; subtitle?: string; note?: string; time?: string;
  image?: string; lines?: string[]; links?: {label:string;url:string}[];
  action?: 'recipe' | 'contest' | 'quiz'; entryId?: number;
}
interface Recipe { title:string; ingredients:string[]; instructions:string[]; sourceUrl:string; }
interface Section { key:string; label:string; description:string; items:SharedItem[]; }

@Component({
  selector:'app-partner-shares',
  imports:[DatePipe,CollectionBrowserComponent,LoadingIndicatorComponent],
  templateUrl:'./partner-shares.html',styleUrl:'./partner-shares.scss',
})
export class PartnerSharesComponent {
  readonly partnerName=input('Your person');
  readonly openRecipe=output<number>();
  readonly openContest=output<number>();
  readonly openQuiz=output<number>();
  readonly sharing=inject(SharingService);
  private readonly http=inject(HttpClient);
  readonly collection=new CollectionBrowser();
  readonly selected=signal('');
  readonly busy=signal(false);
  readonly failed=signal<string[]>([]);
  private readonly data=signal<Record<string,SharedItem[]>>({});
  private version=0;
  private destroyed=false;

  constructor() {
    effect(()=>{
      const state=this.sharing.state();
      // The existing sharing poll also refreshes this visible overview.
      if(!state?.connected) { ++this.version; this.data.set({}); this.busy.set(false); return; }
      untracked(()=>void this.load());
    });
    inject(DestroyRef).onDestroy(()=>{this.destroyed=true;++this.version;});
  }

  private rating(stars:number|null|undefined):string { return stars ? this.partnerName()+' rated it '+stars+'/5' : ''; }
  private watchItem(title:WatchTitle,id:number):SharedItem {
    return {id:String(id),title:title.title,subtitle:title.format,note:title.description,
      links:title.partnerLink ? [{label:'Find it for you',url:title.partnerLink}] : []};
  }

  async load():Promise<void> {
    const version=++this.version;
    this.busy.set(true);this.failed.set([]);this.data.set({});
    const fetch=async<T>(key:string,url:string,map:(result:T)=>SharedItem[])=>{
      const result=await firstValueFrom(this.http.get<T>('/api/auth/'+url));
      if(!this.destroyed && version===this.version) {
        const items=map(result);
        this.data.update(data=>({...data,[key]:items}));
      }
    };
    const requests: {key:string;run:()=>Promise<void>}[]=[
      {key:'watch',run:()=>fetch<{picks:{id:number;mine:boolean;shared:boolean;status:string;title:WatchTitle}[];watched?:{id:number;mine:boolean;title:WatchTitle}[]}>('watch','watch/picks',result=>{
        if(!this.destroyed && version===this.version) this.data.update(data=>({...data,watched:(result.watched ?? []).map(x=>({
          ...this.watchItem(x.title,x.id),links:(x.mine ? x.title.ownLink : x.title.partnerLink) ? [{label:'Find it for you',url:x.mine ? x.title.ownLink : x.title.partnerLink}] : [],subtitle:x.title.format+' · Watched together',
        }))}));
        return result.picks.filter(x=>!x.mine && x.shared).map(x=>({...this.watchItem(x.title,x.id),subtitle:x.title.format+' · '+(x.status==='pending' ? 'Awaiting your response' : x.status)}));
      })},
      {key:'books',run:()=>fetch<{entries:{id:number;mine:boolean;status:string;book:BookTitle;partnerRating?:number|null}[]}>('books','books/shelf',result=>result.entries
        .filter(x=>!x.mine || x.status==='read').map(x=>({id:String(x.id),title:x.book.title,subtitle:x.book.authors.join(', '),
          note:x.status==='read' ? 'Read together' : 'On '+this.partnerName()+"'s bookshelf",lines:[this.rating(x.partnerRating)].filter(Boolean),
          links:[{label:'Book details',url:'https://openlibrary.org'+x.book.id}]})))},
      {key:'recipes',run:()=>fetch<{partnerCookingShared?:boolean;entries:{id:number;recipe:Recipe;partnerMade?:boolean;partnerRating?:number|null}[]}>('recipes','cooking/collection',result=>result.entries.map(x=>({
        id:String(x.id),title:x.recipe.title,subtitle:result.partnerCookingShared && x.partnerMade ? this.partnerName()+' has made this' : 'In your shared recipe collection',
        note:result.partnerCookingShared ? this.rating(x.partnerRating) : '',action:'recipe',entryId:x.id,
      })))},
      {key:'places',run:()=>fetch<{items:{id:number;name:string;location:string;mine:boolean;note?:string;link?:string;identity?:{sourceUrl:string;description:string}}[]}>('places','saved-places',result=>result.items.map(x=>({
        id:String(x.id),title:x.name,subtitle:x.location,note:x.note,lines:['Added by '+(x.mine ? 'you' : this.partnerName()),x.identity?.description ?? ''].filter(Boolean),
        links:[...(x.link ? [{label:'Saved link',url:x.link}] : []),...(x.identity ? [{label:'Wikipedia place',url:x.identity.sourceUrl}] : [])],
      })))},
      {key:'food',run:()=>fetch<{partnerShared:boolean;partnerPreferences:{avoidRedMeat:boolean;excludedIngredients:string[]}|null}>('food','cooking/preferences',result=>{
        const preferences=result.partnerShared ? result.partnerPreferences : null;
        return preferences ? [{id:'food',title:'Food preferences',subtitle:'Shared by '+this.partnerName(),
          lines:[...(preferences.avoidRedMeat ? ['No red meat'] : []),...preferences.excludedIngredients.map(food=>'Avoids '+food)],note:!preferences.avoidRedMeat && !preferences.excludedIngredients.length ? 'No foods excluded.' : undefined}] : [];
      })},
      {key:'photos',run:()=>fetch<{contestId:number;startUtc:string;recipe:Recipe;photos:{mine:boolean;accountId:string;revision:string}[]}[]>('photos','cooking/memories',result=>result.flatMap(x=>x.photos.filter(photo=>!photo.mine).map(photo=>({
        id:String(x.contestId),title:x.recipe.title,subtitle:this.partnerName()+"'s dish photo",time:x.startUtc,
        image:'/api/auth/cooking/memories/'+x.contestId+'/photos/'+encodeURIComponent(photo.accountId)+'?v='+encodeURIComponent(photo.revision),action:'contest',entryId:x.contestId,
      }))))},
      {key:'quizzes',run:()=>fetch<{rounds:{id:number;mine:boolean;title:string;status:string}[]}>('quizzes','quiz/rounds',result=>result.rounds.map(x=>({
        id:String(x.id),title:x.title,subtitle:x.status,note:x.mine ? 'Invited by you' : 'Invited by '+this.partnerName(),action:'quiz',entryId:x.id,
      })))},
    ];
    const results=await Promise.allSettled(requests.map(x=>x.run()));
    if(this.destroyed || version!==this.version)return;
    this.failed.set(requests.filter((_,index)=>results[index].status==='rejected').map(x=>x.key));
    this.busy.set(false);
  }

  readonly sections=computed<Section[]>(()=>{
    const state=this.sharing.state();
    if(!state?.connected)return [];
    const data=this.data();
    const dates:SharedItem[]=(state.outingUpdates ?? []).flatMap(x=>{
      const venue=x.mine ? (x.cancelled && x.cancelledByMe===false ? x.venue : x.replyVenue) : x.venue;
      if(!venue && !(x.mine && x.replyKind==='surprise'))return [];
      return [{id:x.revision,title:venue?.name ?? 'Joining your date',subtitle:venue?.address,
        time:x.venue?.startUtc,note:x.cancelled ? 'Cancelled'+(x.cancellationReason ? ': '+x.cancellationReason : '') : x.mine ? 'Your partner replied'+(x.replyKind==='surprise' ? ' · A surprise' : '') : 'Shared by '+this.partnerName(),
        links:venue ? [{label:'Map',url:venue.mapUrl},...(venue.website ? [{label:'Website',url:venue.website}] : [])] : []}];
    });
    dates.push(...(state.cookingContests ?? []).map(x=>({id:'contest-'+x.id,title:x.recipeTitle || 'Cooking contest',subtitle:x.status,time:x.startUtc,action:'contest' as const,entryId:x.id})));
    return [
      {key:'watch',label:'Watch picks',description:'Titles your person chose to share with you.'},
      {key:'books',label:'Books',description:'Their shared bookshelf, books read together and their ratings.'},
      {key:'recipes',label:'Recipes',description:'Your shared collection, including cooking history and ratings they share.'},
      {key:'places',label:'Places',description:'Ideas saved for both of you.'},
      {key:'dates',label:'Dates & invitations',description:'Their outings, replies and your cooking invitations.'},
      {key:'watched',label:'Watched together',description:'Your shared viewing history.'},
      {key:'photos',label:'Cooking photos',description:'Dish photos your person uploaded for you both.'},
      {key:'food',label:'Food preferences',description:'Only preferences they choose to share.'},
      {key:'quizzes',label:'Quizzes',description:'Your shared rounds. Private drafts and unrevealed answers stay private.'},
    ].map(section=>({...section,items:section.key==='dates' ? dates : data[section.key] ?? []}));
  });
  readonly current=computed(()=>this.sections().find(x=>x.key===this.selected()));
  readonly menuGroups=computed(()=>[
    {title:'Watch & Read',keys:['watch','books','watched']},
    {title:'Cooking',keys:['recipes','photos','food']},
    {title:'Outings',keys:['places','dates']},
    {title:'Get to know each other',keys:['quizzes']},
  ].map(group=>({...group,sections:group.keys.map(key=>this.sections().find(section=>section.key===key)).filter((section):section is Section=>!!section)})));
  readonly filtered=computed(()=>this.current()?.items.filter(x=>this.collection.matches([x.title,x.subtitle,x.note,...(x.lines ?? [])].join(' '))) ?? []);
  view(key:string):void {this.selected.set(key);this.collection.reset();}
  open(item:SharedItem):void {
    if(item.entryId===undefined)return;
    if(item.action==='recipe')this.openRecipe.emit(item.entryId);
    else if(item.action==='contest')this.openContest.emit(item.entryId);
    else if(item.action==='quiz')this.openQuiz.emit(item.entryId);
  }
}
