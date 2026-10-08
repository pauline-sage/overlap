import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { MakeTogetherComponent } from './make-together';

beforeEach(()=>sessionStorage.removeItem('overlap-navigation'));
afterEach(()=>sessionStorage.removeItem('overlap-navigation'));

describe('Drawing progress badge',()=>{
  it('counts paused rounds and own missing photos, and refreshes when returning',async()=>{
    const rounds=[
      {status:'paused',photos:[]},
      {status:'finished',photos:[{mine:false}]},
      {status:'finished',photos:[{mine:true}]},
      {status:'cancelled',photos:[]},
      {status:'finished',currentConnection:false,photos:[{mine:false}]}
    ];
    const http={get:vi.fn(()=>of({connected:true,rounds}))};
    TestBed.configureTestingModule({imports:[MakeTogetherComponent],providers:[{provide:HttpClient,useValue:http}]});
    const f=TestBed.createComponent(MakeTogetherComponent);f.componentRef.setInput('active',true);
    f.detectChanges();await f.whenStable();f.detectChanges();
    const badge=f.nativeElement.querySelector('.ongoing-badge');
    expect(badge.textContent.trim()).toBe('2');expect(badge.getAttribute('aria-label')).toBe('2 unfinished drawing rounds');
    rounds.splice(0,rounds.length);f.componentRef.setInput('active',false);f.detectChanges();
    f.componentRef.setInput('active',true);f.detectChanges();await f.whenStable();f.detectChanges();
    expect(f.nativeElement.querySelector('.ongoing-badge')).toBeNull();f.destroy();
  });
});
