import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { DateCardsComponent } from './date-cards';
import { SharingService } from '../nav-bar/sharing/sharing.service';
import { readableHours } from '../coffee-date/opening-hours';
describe('Activity cards and hours',()=>{
  it('automatically folds elapsed dates into expandable past cards',()=>{
    vi.useFakeTimers();vi.setSystemTime(new Date('2026-10-01T12:00:00Z'));
    const sharing={error:signal(''),busy:signal(false),state:signal({outingUpdates:[{mine:true,revision:'timed',venue:{activity:'Coffee',name:'Cafe',startUtc:'2026-10-01T12:00:01Z'}}]})};
    TestBed.configureTestingModule({imports:[DateCardsComponent],providers:[{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(DateCardsComponent);
    try {
      f.detectChanges();
      expect(f.nativeElement.querySelector('.past-summary')).toBeNull();
      vi.advanceTimersByTime(15000);f.detectChanges();
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
