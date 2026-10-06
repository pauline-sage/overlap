import { TestBed } from '@angular/core/testing';
import { AuthService } from '../../auth.service';
import { SharingService } from '../nav-bar/sharing/sharing.service';
import { CoffeeDateComponent } from './coffee-date';

describe('Personal outing search', () => {
  const auth = { submit: vi.fn() };
  beforeEach(() => {
    vi.resetAllMocks();
    TestBed.configureTestingModule({imports:[CoffeeDateComponent],providers:[
      {provide:AuthService,useValue:auth},
      {provide:SharingService,useValue:{busy:()=>false,error:()=>'',state:()=>({connected:true}),load:vi.fn().mockResolvedValue(undefined)}}
    ]});
  });
  it('uses the proposed date for a private local search',async()=>{
    auth.submit.mockResolvedValue({places:[],examined:0,localStart:'12:00',localEnd:'13:00'});
    const f=TestBed.createComponent(CoffeeDateComponent);
    f.componentRef.setInput('proposal',{revision:'proposal',venue:{activity:'Lunch',startUtc:'2026-10-01T11:00:00Z'}});
    f.detectChanges();
    expect(f.componentInstance.step()).toBe(2);
    f.componentInstance.choose({name:'Lyon',timeZone:'Europe/Paris',token:'local'});
    await f.componentInstance.search();
    expect(auth.submit).toHaveBeenCalledWith('outings/search',expect.objectContaining({proposalRevision:'proposal',activity:'Lunch',localStart:null}));
    f.destroy();
  });
  it('searches for one person without requesting partner information', async () => {
    auth.submit.mockResolvedValue({places:[],examined:6,localStart:'12:00',localEnd:'13:00',checkedUtc:'2026-09-29T12:00Z'});
    const f=TestBed.createComponent(CoffeeDateComponent);
    f.componentInstance.choose({name:'Lyon',timeZone:'Europe/Paris',token:'local'});
    f.componentInstance.when='now';
    await f.componentInstance.search();
    expect(auth.submit).toHaveBeenCalledExactlyOnceWith('outings/search',{
      areaToken:'local',radius:3,activity:'Coffee',localStart:null,startInMinutes:null,minutes:60,includeUnknownHours:true
    });
    f.destroy();
  });
  it('only shares a place explicitly and can keep it private', async () => {
    auth.submit.mockResolvedValue({});
    const f=TestBed.createComponent(CoffeeDateComponent);f.detectChanges();
    const shared=vi.fn();f.componentInstance.shared.subscribe(shared);
    expect(auth.submit).not.toHaveBeenCalled();
    await f.componentInstance.share('venue','signed-venue');
    expect(auth.submit).toHaveBeenLastCalledWith('outings/update',{kind:'venue',venueToken:'signed-venue'});
    expect(shared).toHaveBeenCalledTimes(1);
    await f.componentInstance.share('clear');
    expect(shared).toHaveBeenCalledTimes(1);
    expect(auth.submit).toHaveBeenLastCalledWith('outings/update',{kind:'clear',venueToken:undefined});
    f.destroy();
  });

  it('requires a date when scheduling and moves from mood to location', () => {
    const f=TestBed.createComponent(CoffeeDateComponent);
    const c=f.componentInstance;f.detectChanges();
    const plan=f.nativeElement.querySelectorAll('.when-options input')[1];
    expect(plan.closest('details')).toBeNull();
    plan.click();f.detectChanges();
    expect(c.when).toBe('later');
    c.next();
    expect(c.step()).toBe(1);
    f.detectChanges();
    expect(f.nativeElement.querySelector('input[type=date],select')).toBeNull();
    const calendar=f.nativeElement.querySelector('app-outing-calendar');
    calendar.querySelector('.days button:not(:disabled)').click();
    expect(c.date).toMatch(/^[0-9]{4}-[0-9]{2}-[0-9]{2}$/);
    c.next();f.detectChanges();
    expect(c.step()).toBe(2);
    expect(f.nativeElement.querySelector('#area')).not.toBeNull();
    c.goBack();expect(c.step()).toBe(1);
    f.destroy();
  });
});
