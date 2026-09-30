import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { DateCardsComponent } from './date-cards';
import { SharingService } from '../nav-bar/sharing/sharing.service';
import { readableHours } from '../coffee-date/opening-hours';
describe('Activity cards and hours',()=>{
  it('formats day ranges and overnight hours without guessing special rules',()=>{
    expect(readableHours('Su-Th 11:30-23:00; Fr,Sa 11:30-00:00')).toEqual([
      {days:'Sun � Thu',hours:'11:30 � 23:00'},
      {days:'Fri, Sat',hours:'11:30 � 00:00 (+1 day)'}
    ]);
    expect(readableHours('PH off')[0]).toEqual({days:'As listed',hours:'PH off'});
  });
  it('confirms cancellation with an optional message and separates cancelled dates',async()=>{
    const update={mine:true,revision:'proposal',cancelled:false,venue:{activity:'Coffee',startUtc:new Date(Date.now()+3600000).toISOString(),name:'Cafe'}};
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
    expect(f.nativeElement.textContent).toContain('Cancelled by');
    expect(f.nativeElement.querySelector('.reply-actions')).toBeNull();
    f.destroy();
  });
  it('shows partner, date and venue links and acknowledges only this revision',()=>{
    const sharing={error:signal(''),reply:vi.fn(),busy:signal(false),markRead:vi.fn(),state:signal({partnerName:'Lina',outingUpdates:[{
      mine:false,unread:true,revision:'new-version',venue:{name:'Cafe',address:'Public address',activity:'Lunch',startUtc:new Date(Date.now()+3600000).toISOString(),website:'https://example.test',mapUrl:'https://www.openstreetmap.org'}
    }]})};
    TestBed.configureTestingModule({imports:[DateCardsComponent],providers:[{provide:SharingService,useValue:sharing}]});
    const f=TestBed.createComponent(DateCardsComponent);f.detectChanges();
    expect(f.nativeElement.querySelector('h3').textContent).toBe('Lunch date');
    expect(f.nativeElement.textContent).toContain('With Lina');
    expect(f.nativeElement.querySelector('.date-time strong')).not.toBeNull();
    expect(f.nativeElement.querySelector('.place h4').textContent).toBe('Cafe');
    expect(f.nativeElement.textContent).toContain('Lina picked');
    expect(f.nativeElement.querySelectorAll('a').length).toBe(2);
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
