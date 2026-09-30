import { TestBed } from '@angular/core/testing';
import { DashboardComponent } from './dashboard';
import { AuthService } from '../../auth.service';
import { SharingService } from '../nav-bar/sharing/sharing.service';
describe('Connection first steps',()=>{
  it('opens only after successful acceptance and links to Settings',async()=>{
    const auth={submit:vi.fn().mockRejectedValue(new Error('Unavailable')),together:vi.fn().mockResolvedValue({id:'me',partner:{id:'partner'}})};
    TestBed.configureTestingModule({imports:[DashboardComponent],providers:[{provide:AuthService,useValue:auth}]});
    TestBed.overrideComponent(DashboardComponent,{set:{template:'',providers:[{provide:SharingService,useValue:{load:vi.fn().mockResolvedValue(undefined)}}]}});
    const f=TestBed.createComponent(DashboardComponent);
    await f.componentInstance.accept();
    expect(f.componentInstance.firstSteps()).toBe(false);
    auth.submit.mockResolvedValue({});
    await f.componentInstance.accept();
    expect(f.componentInstance.firstSteps()).toBe(true);
    expect(f.componentInstance.page()).toBe('home');
    f.componentInstance.finishFirstSteps(true);
    expect(f.componentInstance.firstSteps()).toBe(false);
    expect(f.componentInstance.page()).toBe('settings');
    f.destroy();
  });
});
