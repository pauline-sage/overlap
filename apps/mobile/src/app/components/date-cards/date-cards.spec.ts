import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { DateCardsComponent } from './date-cards';
import { SharingService } from '../nav-bar/sharing/sharing.service';
import { readableHours } from '../coffee-date/opening-hours/opening-hours';
describe('Activity cards and hours',()=>{
  it('shares the five-date page limit across outings and contests and leaves notifications complete',()=>{
    const start=(hours:number)=>new Date(Date.now()+hours*3600000).toISOString();
    const sharing={error:signal(''),busy:signal(false),state:signal({outingUpdates:[{mine:true,revision:'outing',venue:{activity:'Coffee',name:'First cafe',startUtc:start(1)}}],cookingContests:Array.from({length:19},(_,i)=>({id:i+1,revision:'contest-'+i,status:'accepted',mine:true,startUtc:start(i+2),recipeTitle:'Recipe '+i}))})};
    TestBed.configureTestingModule({imports:[DateCardsComponent],providers:[{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(DateCardsComponent);const c=f.componentInstance;f.detectChanges();
    expect(f.nativeElement.querySelectorAll('.date-card')).toHaveLength(5);expect(c.displayedGroups()[0].updates).toHaveLength(1);expect(f.nativeElement.textContent).toContain('Page 1 of 4');
    c.collection.page.set(2);f.detectChanges();expect(c.displayedGroups()[0].contests.map(x=>x.id)).toEqual([5,6,7,8,9]);
    const input=f.nativeElement.querySelector('#dates-search') as HTMLInputElement;input.value='First cafe';input.dispatchEvent(new Event('input'));f.detectChanges();
    expect(c.collection.page()).toBe(1);expect(f.nativeElement.querySelectorAll('.date-card')).toHaveLength(1);
    c.collection.search('not found');f.detectChanges();expect(f.nativeElement.textContent).toContain('No matching items');expect(f.nativeElement.textContent).not.toContain('No upcoming dates yet');
    c.viewDates('passed');f.detectChanges();expect(c.collection.page()).toBe(1);expect(c.collection.query()).toBe('');expect(f.nativeElement.textContent).toContain('No passed dates yet');
    f.componentRef.setInput('notifications',true);f.detectChanges();expect(f.nativeElement.querySelector('app-collection-browser')).toBeNull();expect(f.nativeElement.querySelectorAll('.date-card')).toHaveLength(20);f.destroy();
  });
  it('separates Upcoming and Passed tabs and keeps Watch picks only in Notifications',()=>{
    const sharing={error:signal(''),busy:signal(false),state:signal({outingUpdates:[
      {mine:true,revision:'future',venue:{activity:'Coffee',name:'Future cafe',startUtc:new Date(Date.now()+3600000).toISOString()}},
      {mine:true,revision:'past',venue:{activity:'Coffee',name:'Past cafe',startUtc:new Date(Date.now()-3600000).toISOString()}}
    ],moviePicks:[{id:1,revision:'film',shared:true,mine:true,status:'pending',title:{title:'Watch-only film',format:'Movie'}}]})};
    TestBed.configureTestingModule({imports:[DateCardsComponent],providers:[{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(DateCardsComponent);f.detectChanges();
    const tabs=f.nativeElement.querySelectorAll('[role="tab"]');
    expect(Array.from(tabs as NodeListOf<HTMLElement>).map(x=>x.textContent?.trim())).toEqual(['Upcoming','Passed']);
    expect(f.nativeElement.textContent).toContain('Future cafe');
    expect(f.nativeElement.querySelector('.summary-copy>strong').textContent).toBe('Coffee');
    expect(f.nativeElement.querySelector('.invitation-note').textContent).toBe("You've asked your partner out");
    expect(f.nativeElement.textContent).not.toContain('Past cafe');
    expect(f.nativeElement.textContent).not.toContain('Watch-only film');
    const futureDetails=f.nativeElement.querySelector('#past-future');
    expect(futureDetails.hidden).toBe(true);
    f.nativeElement.querySelector('.past-summary').click();f.detectChanges();
    expect(futureDetails.hidden).toBe(false);
    f.nativeElement.querySelector('.past-summary').click();f.detectChanges();
    expect(futureDetails.hidden).toBe(true);
    tabs[0].dispatchEvent(new KeyboardEvent('keydown',{key:'End',bubbles:true}));f.detectChanges();
    expect(tabs[1].getAttribute('aria-selected')).toBe('true');
    expect(f.nativeElement.textContent).not.toContain('Future cafe');
    expect(f.nativeElement.querySelector('#past-past')).not.toBeNull();
    expect(f.nativeElement.textContent).not.toContain('Watch-only film');
    f.componentRef.setInput('notifications',true);f.detectChanges();
    expect(f.nativeElement.querySelector('[role="tablist"]')).toBeNull();
    expect(f.nativeElement.textContent).toContain('Watch-only film');f.destroy();
  });
  it('shows accepted contests in Dates, pending invitations in Notifications, and elapsed contests in Past dates',()=>{
    const future=new Date(Date.now()+3600000).toISOString();
    const contest={id:1,revision:'contest',status:'pending',mine:false,startUtc:future,timeZone:'UTC',availableMinutes:60,unread:true,dismissed:false};
    const sharing={error:signal(''),busy:signal(false),state:signal({cookingContests:[contest]}),contestAction:vi.fn(),markRead:vi.fn()};
    TestBed.configureTestingModule({imports:[DateCardsComponent],providers:[{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(DateCardsComponent);f.detectChanges();expect(f.nativeElement.querySelector('.contest-card')).toBeNull();
    f.componentRef.setInput('notifications',true);f.detectChanges();expect(f.nativeElement.textContent).not.toContain('No shared updates yet');
    const accept=Array.from(f.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>).find(x=>x.textContent?.includes('Accept contest'))!;accept.click();expect(sharing.contestAction).toHaveBeenCalledWith(contest,'accept');
    sharing.state.set({cookingContests:[{...contest,status:'accepted',dismissed:true}]});f.componentRef.setInput('notifications',false);f.detectChanges();
    expect(f.componentInstance.groups()[0].contests).toHaveLength(1);expect(f.nativeElement.querySelector('.contest-card')).not.toBeNull();expect(f.nativeElement.textContent).not.toContain('No upcoming dates yet');
    const open=vi.fn();f.componentInstance.openContest.subscribe(open);f.nativeElement.querySelector('.contest-actions button').click();expect(open).toHaveBeenCalledWith(1);
    sharing.state.set({cookingContests:[{...contest,status:'accepted',startUtc:new Date(Date.now()-3600000).toISOString()}]});f.detectChanges();expect(f.componentInstance.groups().find(x=>x.label==='Past dates')?.contests).toHaveLength(1);
    sharing.state.set({cookingContests:[{...contest,status:'cancelled'}]});f.detectChanges();expect(f.nativeElement.querySelector('.contest-card')).toBeNull();f.destroy();
  });
  it('shows recipe news and opens its saved recipe without an empty-feed message',()=>{
    const sharing={error:signal(''),busy:signal(false),state:signal({outingUpdates:[],moviePicks:[],recipeShares:[{id:7,title:'Flan',revision:'recipe-revision',unread:true,dismissed:false}]}),markRead:vi.fn()};
    TestBed.configureTestingModule({imports:[DateCardsComponent],providers:[{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(DateCardsComponent);f.componentRef.setInput('notifications',true);f.detectChanges();
    const opened=vi.fn();f.componentInstance.openCooking.subscribe(opened);
    expect(f.nativeElement.textContent).toContain('Flan');expect(f.nativeElement.textContent).not.toContain('No shared updates yet');
    Array.from(f.nativeElement.querySelectorAll('button')).find((x:any)=>x.textContent.includes('View in Cooking')) && (Array.from(f.nativeElement.querySelectorAll('button')).find((x:any)=>x.textContent.includes('View in Cooking')) as HTMLButtonElement).click();
    expect(opened).toHaveBeenCalledWith(7);f.destroy();
  });
  it('automatically folds elapsed dates into expandable past cards',()=>{
    vi.useFakeTimers();vi.setSystemTime(new Date('2026-10-01T12:00:00Z'));
    const sharing={error:signal(''),busy:signal(false),state:signal({outingUpdates:[{mine:true,revision:'timed',venue:{activity:'Coffee',name:'Cafe',startUtc:'2026-10-01T12:00:01Z'}}]})};
    TestBed.configureTestingModule({imports:[DateCardsComponent],providers:[{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(DateCardsComponent);
    try {
      f.detectChanges();
      expect(f.nativeElement.querySelector('.past-summary').getAttribute('aria-expanded')).toBe('false');
      vi.advanceTimersByTime(15000);f.componentInstance.dateTab.set('passed');f.detectChanges();
      const summary=f.nativeElement.querySelector('.past-summary');
      expect(summary.textContent).toContain('Coffee');
      expect(summary.textContent).toContain('1 Oct 2026');
      expect(f.nativeElement.querySelector('#past-timed').hidden).toBe(true);
      summary.click();f.detectChanges();
      expect(f.nativeElement.querySelector('#past-timed').hidden).toBe(false);
      expect(summary.getAttribute('aria-expanded')).toBe('true');
    } finally {f.destroy();vi.useRealTimers();}
  });
  it('formats day ranges and overnight hours without guessing special rules',()=>{
    expect(readableHours('Su-Th 11:30-23:00; Fr,Sa 11:30-00:00')).toEqual([
      {days:'Sun – Thu',hours:'11:30 – 23:00'},
      {days:'Fri, Sat',hours:'11:30 – 00:00 (+1 day)'}
    ]);
    expect(readableHours('PH off')[0]).toEqual({days:'As listed',hours:'PH off'});
  });
  it('confirms cancellation with an optional message and removes cancelled dates while keeping the cancellation notification',async()=>{
    const update={mine:true,revision:'proposal',cancelled:false,dismissed:false,venue:{activity:'Coffee',startUtc:new Date(Date.now()+3600000).toISOString(),name:'Cafe'}};
    const sharing={error:signal(''),busy:signal(false),cancel:vi.fn().mockResolvedValue(true),state:signal({outingUpdates:[update]})};
    TestBed.configureTestingModule({imports:[DateCardsComponent],providers:[{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(DateCardsComponent);f.detectChanges();
    f.nativeElement.querySelector('button.cancel-date').click();f.detectChanges();
    expect(sharing.cancel).not.toHaveBeenCalled();
    f.componentInstance.reason='Something came up';
    await f.componentInstance.cancel(update as any);
    expect(sharing.cancel).toHaveBeenCalledWith('proposal','Something came up');
    update.cancelled=true;f.detectChanges();
    expect(f.componentInstance.groups().find(x=>x.label==='Upcoming dates')?.updates).toHaveLength(0);
    expect(f.nativeElement.querySelector('.date-card')).toBeNull();
    f.componentRef.setInput('notifications',true);f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Cancelled by');
    update.dismissed=true;sharing.state.set({outingUpdates:[{...update}]});f.detectChanges();
    expect(f.nativeElement.querySelector('.date-card')).toBeNull();
    expect(f.nativeElement.textContent).toContain('No shared updates yet.');
    expect(f.nativeElement.querySelector('.reply-actions')).toBeNull();
    f.destroy();
  });
  it('shows partner, date and venue links and acknowledges only this revision',()=>{
    const sharing={error:signal(''),reply:vi.fn(),busy:signal(false),markRead:vi.fn(),state:signal({partnerName:'Lina',outingUpdates:[{
      mine:false,unread:true,revision:'new-version',replyKind:'picked',replyVenue:{name:'My cafe',address:'My public venue',mapUrl:'https://example.test/my-cafe'},venue:{name:'Cafe',address:'Public address',activity:'Lunch',startUtc:new Date(Date.now()+3600000).toISOString(),website:'https://example.test',mapUrl:'https://www.openstreetmap.org'}
    }]})};
    TestBed.configureTestingModule({imports:[DateCardsComponent],providers:[{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(DateCardsComponent);f.detectChanges();
    expect(f.nativeElement.querySelector('h3').textContent).toBe('Lunch with Lina');
    expect(f.nativeElement.textContent).toContain('with Lina');
    expect(f.nativeElement.querySelector('.date-time strong')).not.toBeNull();
    expect(f.componentInstance.localTime('2026-10-01T16:00:00Z','America/New_York')).toContain('12:00');
    expect(f.componentInstance.localTime('2026-10-01T16:00:00Z','Europe/Paris')).toContain('18:00');
    expect(f.componentInstance.localTime('2026-03-15T16:00:00Z','America/New_York')).toContain('12:00');
    expect(f.componentInstance.localTime('2026-03-15T16:00:00Z','Europe/Paris')).toContain('17:00');
    expect(f.nativeElement.querySelector('.place h4').textContent).toBe('Cafe');
    expect(f.nativeElement.textContent).toContain('Lina picked');
    expect(f.nativeElement.querySelectorAll('a').length).toBe(3);
    expect(f.nativeElement.querySelector('.reply h4').textContent).toBe('My cafe');
    expect(f.nativeElement.querySelector('.place-actions .cancel-date')).not.toBeNull();
    f.nativeElement.querySelector('.read').click();
    expect(sharing.markRead).toHaveBeenCalledWith('outing','new-version');
    const proposal=f.componentInstance.sharing.state()!.outingUpdates![0];
    const emit=vi.spyOn(f.componentInstance.pickYours,'emit');
    if(f.componentInstance.canReply(proposal)) {
      const buttons=f.nativeElement.querySelectorAll('.reply-actions button');
      buttons[0].click();expect(emit).toHaveBeenCalledWith(proposal);
      buttons[1].click();expect(sharing.reply).toHaveBeenCalledWith('new-version','surprise');
    }
    f.destroy();
  });
});
