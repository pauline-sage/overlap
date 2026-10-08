import { TestBed } from '@angular/core/testing';
import { SharingService } from '../nav-bar/sharing/sharing.service';
import { ProfileComponent } from './profile';
import { HttpClient } from '@angular/common/http';

describe('Partner profile', () => {
  it('requires acknowledging separate ending conditions before emitting leave', async () => {
    await TestBed.configureTestingModule({imports:[ProfileComponent],providers:[{provide:SharingService,useValue:{state:()=>null,error:()=>''}},{provide:HttpClient,useValue:{get:vi.fn()}}]}).compileComponents();
    const fixture=TestBed.createComponent(ProfileComponent);
    fixture.componentRef.setInput('greetingName','Person');
    fixture.componentRef.setInput('together',{id:'self',hasPhoto:false,invitationExpiresUtc:null,partner:{id:'other',displayName:null,hasPhoto:false}});
    fixture.detectChanges();
    const leave=fixture.nativeElement.querySelector('.connection-heading button[aria-label="End connection"]') as HTMLButtonElement;
    leave.click();fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#end-connection-heading')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('#partner-link')).toBeNull();
    const confirm=Array.from(fixture.nativeElement.querySelectorAll('button') as NodeListOf<HTMLButtonElement>).find(button=>button.textContent?.trim()==='End connection')!;
    expect(confirm.disabled).toBe(true);
    const checkbox=fixture.nativeElement.querySelector('input[type=checkbox]') as HTMLInputElement;
    checkbox.click();fixture.detectChanges();expect(confirm.disabled).toBe(false);
    const actions:string[]=[];fixture.componentInstance.action.subscribe(action=>actions.push(action));
    confirm.click();expect(actions).toEqual(['leave']);
  });
  it('opens the connected partner read-only, returns back, and clears the view on disconnect', async () => {
    await TestBed.configureTestingModule({imports:[ProfileComponent], providers:[{provide:SharingService,useValue:{state:()=>null,error:()=>""}},{provide:HttpClient,useValue:{get:vi.fn()}}]}).compileComponents();
    const f=TestBed.createComponent(ProfileComponent);
    f.componentRef.setInput('greetingName','Lina');
    const own={id:'self',hasPhoto:false,invitationExpiresUtc:null};
    f.componentRef.setInput('together',{...own,partner:{id:'partner',displayName:'Juu',hasPhoto:true,online:true}});
    f.detectChanges();
    f.nativeElement.querySelector('#partner-link').click(); f.detectChanges();
    expect(f.nativeElement.querySelector('#partner-heading').textContent).toContain("Juu's profile");
    expect(f.nativeElement.querySelector('img').getAttribute('src')).toContain('/photo/partner');
    expect(f.nativeElement.querySelector('.presence-dot.online')).toBeTruthy();
    expect(f.nativeElement.querySelector('input[type=file]')).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('Edit display name');
    f.nativeElement.querySelector('.back').click(); f.detectChanges();
    expect(f.nativeElement.querySelector('#partner-link')).toBeTruthy();
    f.nativeElement.querySelector('#partner-link').click(); f.detectChanges();
    f.componentRef.setInput('together',{...own,partner:null}); f.detectChanges();
    expect(f.nativeElement.querySelector('#partner-heading')).toBeNull();
    expect(f.componentInstance.viewingPartner()).toBe(false);
  });
});
