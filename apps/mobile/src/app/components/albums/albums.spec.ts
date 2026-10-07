import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { AlbumsComponent } from './albums';
import { StayInTogetherComponent } from '../stay-in-together/stay-in-together';
describe('Shared albums',()=>{
  const http={get:vi.fn()};
  beforeEach(()=>{
    vi.resetAllMocks();
    http.get.mockImplementation(url=>of(url.endsWith('drawing')?{connected:true,serverUtc:'2026-10-07T10:00:00Z',rounds:[{id:2,prompt:'A dragon',photos:[{accountId:'self',mine:true,revision:'draw',uploadedUtc:'2026-10-07T09:00:00Z'}]}]}:[{contestId:3,recipe:{title:'Soup'},photos:[{accountId:'partner',mine:false,revision:'cook',uploadedUtc:'2026-10-06T09:00:00Z'}]}]));
    TestBed.configureTestingModule({imports:[AlbumsComponent,StayInTogetherComponent],providers:[{provide:HttpClient,useValue:http}]});
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
