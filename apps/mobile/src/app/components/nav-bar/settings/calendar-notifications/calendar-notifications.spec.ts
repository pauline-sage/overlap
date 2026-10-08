import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { AuthService } from '../../../../auth.service';
import { CalendarNotificationsComponent } from './calendar-notifications';

it('loads an owner preference, saves opt-in/time and keeps the old toggle on failure',async()=>{
  const auth={submit:vi.fn().mockResolvedValue({})};const http={get:vi.fn().mockReturnValue(of({dailyEnabled:false,timeZone:'America/New_York',notificationTimeZone:'Europe/Paris',notifyMinutes:540,zones:['Europe/Paris','America/New_York']}))};
  TestBed.configureTestingModule({imports:[CalendarNotificationsComponent],providers:[{provide:AuthService,useValue:auth},{provide:HttpClient,useValue:http}]});
  const f=TestBed.createComponent(CalendarNotificationsComponent);f.detectChanges();await f.whenStable();f.detectChanges();const c=f.componentInstance;
  expect(c.zone).toBe('Europe/Paris');expect(f.nativeElement.textContent).not.toContain('Reminder timezone');expect(c.enabled()).toBe(false);await c.toggle();expect(auth.submit).toHaveBeenLastCalledWith('calendar/preferences',{dailyEnabled:true,timeZone:'Europe/Paris',notifyMinutes:540});
  f.detectChanges();expect(f.nativeElement.textContent).toContain('This timezone only affects reminders.');
  c.time='08:30';await c.save();expect(auth.submit).toHaveBeenLastCalledWith('calendar/preferences',{dailyEnabled:true,timeZone:'Europe/Paris',notifyMinutes:510});
  auth.submit.mockRejectedValue(new Error('Unavailable'));await c.toggle();expect(c.enabled()).toBe(true);expect(c.error()).not.toBe('');f.destroy();
});
