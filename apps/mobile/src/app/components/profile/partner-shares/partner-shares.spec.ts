import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { of, throwError, Subject } from 'rxjs';
import { SharingService } from '../../nav-bar/sharing/sharing.service';
import { PartnerSharesComponent } from './partner-shares';

describe('Partner shared collections',()=>{
  function setup() {
    const sharing={state:signal<any>({connected:true,outingUpdates:[
      {mine:false,revision:'incoming',dismissed:true,venue:{name:'Shared cafe',address:'City',mapUrl:'https://example.com/map',startUtc:'2026-10-07T12:00:00Z'}},
      {mine:true,revision:'mine',venue:{name:'My own proposal'}},
      {mine:true,revision:'cancelled',cancelled:true,cancelledByMe:false,cancellationReason:'Busy',venue:{name:'Cancelled cafe'}},
    ]}),load:vi.fn()};
    const responses:Record<string,unknown>={
      'watch/picks':{picks:[{id:1,mine:true,shared:false,title:{title:'Private owner pick'}},
        ...Array.from({length:12},(_,i)=>({id:i+2,mine:false,shared:true,status:'pending',title:{title:'Partner film '+i,format:'Movie',description:'Summary',partnerLink:'https://example.com/watch'}}))],watched:[{id:1,mine:true,title:{title:'Watched together film',format:'Movie'}}]},
      'books/shelf':{entries:[{id:1,mine:true,status:'planned',book:{title:'My private shelf',authors:[]}},
        {id:2,mine:false,status:'planned',book:{id:'/works/2',title:'Partner book',authors:['Author']}},
        {id:3,mine:true,status:'read',partnerRating:4,book:{id:'/works/3',title:'Read together book',authors:[]}}]},
      'cooking/collection':{partnerCookingShared:false,entries:[{id:1,partnerMade:true,partnerRating:5,recipe:{title:'Shared recipe'}}]},
      'saved-places':{items:[{id:1,mine:false,name:'Partner museum',location:'City'}]},
      'cooking/preferences':{partnerShared:false,partnerPreferences:{avoidRedMeat:true,excludedIngredients:['Private food']}},
      'cooking/memories':[{contestId:1,recipe:{title:'Our dinner'},photos:[{mine:true,accountId:'self',revision:'own'},{mine:false,accountId:'partner',revision:'partner'}]}],
      'quiz/rounds':{rounds:[{id:1,mine:false,title:'Invited quiz',status:'pending'}]},
    };
    const http={get:vi.fn((url:string)=>of(responses[url.replace('/api/auth/','')]))};
    TestBed.configureTestingModule({imports:[PartnerSharesComponent],providers:[{provide:SharingService,useValue:sharing},{provide:HttpClient,useValue:http}]});
    const fixture=TestBed.createComponent(PartnerSharesComponent);fixture.componentRef.setInput('partnerName','Ju');
    return {fixture,sharing,http,responses};
  }

  it('shows authorized partner content and shared histories, five at a time, without private owner content',async()=>{
    const {fixture:f,http}=setup();f.detectChanges();await f.whenStable();f.detectChanges();const c=f.componentInstance;
    expect(f.nativeElement.querySelectorAll('.menu-row')).toHaveLength(9);expect(f.nativeElement.querySelectorAll('article')).toHaveLength(0);
    f.nativeElement.querySelector('.menu-row').click();f.detectChanges();
    expect(f.nativeElement.querySelectorAll('article')).toHaveLength(5);expect(f.nativeElement.textContent).toContain('Page 1 of 3');expect(f.nativeElement.textContent).not.toContain('Private owner pick');
    const next=Array.from(f.nativeElement.querySelectorAll('app-collection-browser button') as NodeListOf<HTMLButtonElement>).find(x=>x.textContent==='Next')!;next.click();f.detectChanges();expect(c.collection.slice(c.filtered())[0].title).toBe('Partner film 5');
    c.collection.search('Partner film 11');f.detectChanges();expect(f.nativeElement.querySelectorAll('article')).toHaveLength(1);
    c.view('books');f.detectChanges();expect(f.nativeElement.textContent).toContain('Partner book');expect(f.nativeElement.textContent).toContain('Read together book');expect(f.nativeElement.textContent).toContain('Ju rated it 4/5');expect(f.nativeElement.textContent).not.toContain('My private shelf');
    c.view('recipes');f.detectChanges();expect(f.nativeElement.textContent).toContain('Shared recipe');expect(f.nativeElement.textContent).not.toContain('has made this');expect(f.nativeElement.textContent).not.toContain('5/5');
    c.view('food');f.detectChanges();expect(f.nativeElement.textContent).not.toContain('Private food');
    c.view('dates');f.detectChanges();expect(f.nativeElement.textContent).toContain('Shared cafe');expect(f.nativeElement.textContent).toContain('Cancelled cafe');expect(f.nativeElement.textContent).not.toContain('My own proposal');
    c.view('photos');f.detectChanges();expect(f.nativeElement.querySelector('img').getAttribute('src')).toContain('/photos/partner');
    c.view('places');f.detectChanges();expect(f.nativeElement.textContent).toContain('Partner museum');
    c.view('watched');f.detectChanges();expect(f.nativeElement.textContent).toContain('Watched together film');
    c.view('quizzes');f.detectChanges();const opened=vi.fn();c.openQuiz.subscribe(opened);f.nativeElement.querySelector('article button').click();expect(opened).toHaveBeenCalledWith(1);
    expect(http.get).not.toHaveBeenCalledWith('/api/auth/quiz/templates');
    f.nativeElement.querySelector('.back').click();f.detectChanges();expect(f.nativeElement.querySelectorAll('.menu-row')).toHaveLength(9);expect(f.nativeElement.querySelectorAll('article')).toHaveLength(0);f.destroy();
  });

  it('refreshes revoked shares, clears failed collections and ignores responses after disconnect',async()=>{
    const {fixture:f,sharing,http,responses}=setup();f.detectChanges();await f.whenStable();f.detectChanges();const c=f.componentInstance;c.view('watch');f.detectChanges();
    responses['watch/picks']={picks:[]};sharing.state.set({...sharing.state()});f.detectChanges();await f.whenStable();f.detectChanges();expect(f.nativeElement.querySelectorAll('article')).toHaveLength(0);
    http.get.mockImplementation(()=>throwError(()=>new Error('Unavailable')));await c.load();f.detectChanges();expect(c.failed()).toContain('watch');expect(f.nativeElement.textContent).toContain('could not be loaded');
    const pending:Subject<unknown>[]=[];
    http.get.mockImplementation(()=>{const request=new Subject<unknown>();pending.push(request);return request;});
    // A disconnect invalidates all outstanding collection requests.
    const loading=c.load();sharing.state.set({connected:false});f.detectChanges();
    pending.forEach(request=>{request.next({});request.complete();});await loading;f.detectChanges();
    expect(c.sections()).toEqual([]);expect(f.nativeElement.querySelectorAll('article')).toHaveLength(0);f.destroy();
  });
});
