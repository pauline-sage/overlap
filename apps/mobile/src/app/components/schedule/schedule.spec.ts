import { TestBed } from '@angular/core/testing';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { of } from 'rxjs';
import { AuthService } from '../../auth.service';
import { ScheduleComponent } from './schedule';

describe('Shared calendar',()=>{
  const auth={submit:vi.fn()};const http={get:vi.fn()};let items:any[]=[];
  beforeEach(()=>{
    vi.resetAllMocks();items=[];auth.submit.mockResolvedValue({});
    http.get.mockImplementation(url=>of(url.endsWith('preferences') ? {zones:['Europe/Paris','America/New_York']} : {connected:true,items,ownToday:0,partnerToday:0}));
    TestBed.configureTestingModule({imports:[ScheduleComponent],providers:[{provide:AuthService,useValue:auth},{provide:HttpClient,useValue:http}]});
  });
  async function render(){const f=TestBed.createComponent(ScheduleComponent);f.componentRef.setInput('opened',true);f.componentRef.setInput('partnerName','Pauline');f.detectChanges();await f.whenStable();f.detectChanges();return f;}
  it('uses the saved profile timezone for dates, times and new appointments',async()=>{
    http.get.mockImplementation(url=>of(url.endsWith('preferences') ? {timeZone:'America/Sao_Paulo',zones:['America/Sao_Paulo']} : {connected:true,items,ownToday:0,partnerToday:0}));
    const f=await render();const c=f.componentInstance;expect(c.zone).toBe('America/Sao_Paulo');expect(c.instantDay('2026-10-07T01:00:00Z')).toBe('2026-10-06');expect(c.clockTime('2026-10-07T01:00:00Z')).toBe('22:00');c.add();expect(c.timeZone).toBe('America/Sao_Paulo');f.destroy();
  });
  it('opens a real empty calendar and saves yearly all-day birthdays through the API',async()=>{
    const f=await render();const c=f.componentInstance;
    expect(f.nativeElement.textContent).not.toContain('Sample');expect(f.nativeElement.textContent).toContain('Nothing planned');
    c.add();c.title='Birthday';c.allDay=true;c.yearly=true;c.entryDate='2000-02-29';c.endDate=c.entryDate;c.timeZone='Europe/Paris';await c.save();
    expect(auth.submit).toHaveBeenCalledWith('calendar',expect.objectContaining({title:'Birthday',date:'2000-02-29',allDay:true,yearly:true,timeZone:'Europe/Paris'}));expect(c.notice()).toContain('saved and shared');expect(c.formOpened()).toBe(false);
    c.monthOpened.set(true);f.detectChanges();expect(f.nativeElement.querySelector('.month-calendar')).not.toBeNull();f.destroy();
  });
  it('keeps all-day dates stable, pages entries and exposes editing only to their owner',async()=>{
    const f=await render();const c=f.componentInstance;const date=c.selected();const end=new Date(date+'T12:00');end.setDate(end.getDate()+1);
    items=Array.from({length:6},(_,i)=>({id:i+1,title:'Plan '+i,mine:i===0,allDay:true,yearly:i===0,timeZone:'Pacific/Honolulu',date,endDate:c.dayKey(end),sourceDate:date,sourceEndDate:date,startTime:'00:00',endTime:'00:00',startUtc:null,endUtc:null,revision:'r'+i}));await c.load();f.detectChanges();
    expect(f.nativeElement.querySelectorAll('.appointment')).toHaveLength(5);expect(f.nativeElement.textContent).toContain('All day');expect(f.nativeElement.textContent).toContain('Yearly');expect(f.nativeElement.querySelectorAll('.appointment .form-actions')).toHaveLength(1);
    c.pager.page.set(2);f.detectChanges();expect(f.nativeElement.querySelectorAll('.appointment')).toHaveLength(1);
    c.pager.page.set(1);const own=c.agenda()[0];c.edit(own);expect(c.timeZone).toBe('Pacific/Honolulu');c.title='Updated';await c.save();expect(auth.submit).toHaveBeenCalledWith('calendar/1',expect.objectContaining({revision:'r0',title:'Updated'}));
    auth.submit.mockClear();await c.remove(own);expect(auth.submit).not.toHaveBeenCalled();c.removing.set(own.id);await c.remove(own);expect(auth.submit).toHaveBeenCalledWith('calendar/1/remove',{revision:'r0'});f.destroy();
  });
  it('retains a failed draft and includes existing shared dates without inventing end times',async()=>{
    const f=await render();const c=f.componentInstance;c.add();c.title='Doctor';auth.submit.mockRejectedValue(new HttpErrorResponse({status:409,error:{title:'This appointment changed.'}}));await c.save();expect(c.formOpened()).toBe(true);expect(c.title).toBe('Doctor');expect(c.error()).toBe('This appointment changed.');
    const start=new Date(c.selected()+'T18:00').toISOString();f.componentRef.setInput('outings',[{mine:false,cancelled:false,revision:'real',venue:{name:'Cafe',activity:'Coffee',startUtc:start}},{mine:true,cancelled:true,revision:'cancel',venue:{name:'Cancelled',startUtc:start}}]);f.detectChanges();expect(c.agenda()).toHaveLength(1);expect(c.agenda()[0].end).toBeUndefined();expect(f.nativeElement.textContent).toContain('From Dates');f.destroy();
  });
});
