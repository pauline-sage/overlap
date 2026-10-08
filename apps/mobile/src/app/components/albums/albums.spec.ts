import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { AlbumsComponent } from './albums';
import { StayInTogetherComponent } from '../stay-in-together/stay-in-together';
describe('Shared albums',()=>{
  beforeEach(()=>sessionStorage.removeItem('overlap-navigation'));
  afterEach(()=>sessionStorage.removeItem('overlap-navigation'));
  const http={get:vi.fn()};
  beforeEach(()=>{
    vi.resetAllMocks();
    http.get.mockImplementation(url=>of(url.endsWith('drawing')?{connected:true,serverUtc:'2026-10-07T10:00:00Z',rounds:[{id:2,prompt:'A dragon',photos:[{accountId:'self',mine:true,revision:'draw',uploadedUtc:'2026-10-07T09:00:00Z'}]}]}:[{contestId:3,recipe:{title:'Soup'},photos:[{accountId:'partner',mine:false,revision:'cook',uploadedUtc:'2026-10-06T09:00:00Z'}]}]));
    TestBed.configureTestingModule({imports:[AlbumsComponent,StayInTogetherComponent],providers:[{provide:HttpClient,useValue:http}]});
  });
  it('groups both drawings under one title and date, and keeps repeated prompts as separate rounds',async()=>{
    http.get.mockImplementation(url=>of(url.endsWith('drawing')?{rounds:[
      {id:2,prompt:'A waffle',currentConnection:false,startUtc:'2026-10-08T09:00:00Z',photos:[{accountId:'self',mine:true,revision:'own',uploadedUtc:'2026-10-08T10:00:00Z'},{accountId:'partner',mine:false,revision:'partner',uploadedUtc:'2026-10-09T10:00:00Z'}]},
      {id:4,prompt:'A waffle',startUtc:'2026-10-10T09:00:00Z',photos:[{accountId:'self',mine:true,revision:'other',uploadedUtc:'2026-10-10T10:00:00Z'}]}
    ]}:[]));
    const f=TestBed.createComponent(AlbumsComponent);f.componentRef.setInput('ownName','Renée');f.componentRef.setInput('partnerName','André');
    f.detectChanges();await vi.waitFor(()=>expect(f.componentInstance.loading()).toBe(false));f.detectChanges();
    expect(f.componentInstance.error()).toBe('');
    const cards=f.nativeElement.querySelectorAll('.drawing-card');expect(cards).toHaveLength(2);
    const paired=[...cards].find((card:any)=>card.querySelectorAll('img').length===2) as HTMLElement;
    expect(paired.querySelectorAll('.caption strong')).toHaveLength(1);expect(paired.querySelectorAll('.caption small')).toHaveLength(1);
    expect(paired.textContent).toContain('Renée');expect(paired.textContent).toContain('Partner');expect(paired.textContent).not.toContain('André');
    expect(f.componentInstance.photos().find(x=>x.id===2)?.date).toBe('2026-10-08T09:00:00Z');f.destroy();
  });
  it('puts an icon-only refresh beside the title and omits repeated privacy copy',async()=>{
    const f=TestBed.createComponent(AlbumsComponent);f.detectChanges();await vi.waitFor(()=>expect(f.componentInstance.loading()).toBe(false));f.detectChanges();
    const refresh=f.nativeElement.querySelector('.album-heading .refresh');
    expect(refresh.getAttribute('aria-label')).toBe('Refresh albums');expect(refresh.textContent.trim()).toBe('');expect(refresh.querySelector('svg')).not.toBeNull();
    expect(f.nativeElement.textContent).not.toContain('Disconnecting');expect(f.nativeElement.textContent).not.toContain('Signing out keeps');
    refresh.click();await vi.waitFor(()=>expect(http.get).toHaveBeenCalledTimes(4));f.destroy();
  });
  it('shows cooking and drawing photos in one collection with category filters',async()=>{
    const f=TestBed.createComponent(AlbumsComponent);f.componentRef.setInput('ownName','Renée');f.detectChanges();await vi.waitFor(()=>expect(f.componentInstance.loading()).toBe(false));f.detectChanges();
    expect(f.componentInstance.error()).toBe('');expect(f.componentInstance.photos()).toHaveLength(2);expect(f.nativeElement.querySelectorAll('.photo-card')).toHaveLength(2);expect(f.nativeElement.textContent).toContain('Renée');
    f.componentInstance.filter('cooking');f.detectChanges();expect(f.nativeElement.querySelectorAll('.photo-card')).toHaveLength(1);expect(f.nativeElement.textContent).toContain('Soup');
    f.componentInstance.filter('drawing');f.detectChanges();expect(f.nativeElement.querySelectorAll('.photo-card')).toHaveLength(1);expect(f.nativeElement.textContent).toContain('A dragon');f.destroy();
  });
  it('places Albums after the main category controls rather than inside the header',async()=>{
    const f=TestBed.createComponent(StayInTogetherComponent);f.componentRef.setInput('active',true);f.componentInstance.group.set('albums');f.detectChanges();await f.whenStable();f.detectChanges();
    expect(f.nativeElement.querySelector('header.page-banner app-albums')).toBeNull();expect(f.nativeElement.querySelector('.groups > app-albums')).not.toBeNull();f.destroy();
  });
});
