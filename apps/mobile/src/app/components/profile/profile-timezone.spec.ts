import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { AuthService } from '../../auth.service';
import { ProfileTimezoneComponent } from './profile-timezone';

it('loads and saves a profile timezone without changing reminder opt-in or time',async()=>{
  const auth={submit:vi.fn().mockResolvedValue({})};
  TestBed.configureTestingModule({imports:[ProfileTimezoneComponent],providers:[{provide:AuthService,useValue:auth},{provide:HttpClient,useValue:{get:()=>of({timeZone:'Europe/Paris',zones:['Europe/Paris','America/Sao_Paulo']})}}]});
  const f=TestBed.createComponent(ProfileTimezoneComponent);f.detectChanges();await f.whenStable();
  expect(f.componentInstance.zone).toBe('Europe/Paris');f.componentInstance.zone='America/Sao_Paulo';await f.componentInstance.save();
  expect(auth.submit).toHaveBeenCalledWith('calendar/timezone',{timeZone:'America/Sao_Paulo'});
  let complete!:()=>void;auth.submit.mockImplementationOnce(()=>new Promise<void>(resolve=>complete=resolve));
  const pending=f.componentInstance.save();f.detectChanges();expect(f.nativeElement.querySelector('app-loading-indicator')).not.toBeNull();
  complete();await pending;f.detectChanges();expect(f.nativeElement.querySelector('app-loading-indicator')).toBeNull();
  auth.submit.mockRejectedValueOnce(new Error('offline'));await f.componentInstance.save();expect(f.componentInstance.error()).toContain('Could not save');f.destroy();
});
