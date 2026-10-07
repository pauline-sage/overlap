import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { AuthService } from '../../../auth.service';
import { SharingService } from './sharing.service';
import { SharingComponent } from './sharing';

describe('Owner sharing preferences', () => {
  const auth = { submit: vi.fn().mockResolvedValue({}) };
  const state = { connected: true, outgoing: [{ category: 'presence', status: 'pending' }], incoming: [{ category: 'profile', status: 'pending' }], receivedPicks: 2 };
  const http = { get: vi.fn() };
  beforeEach(() => {
    vi.clearAllMocks();
    auth.submit.mockResolvedValue({});
    http.get.mockReturnValue(of(state));
    TestBed.configureTestingModule({ imports: [SharingComponent], providers: [SharingService,
      { provide: AuthService, useValue: auth }, { provide: HttpClient, useValue: http }
    ] });
  });
  it('enables sharing directly, with old pending requests off', async () => {
    const f = TestBed.createComponent(SharingComponent); await f.whenStable(); f.detectChanges();
    const controls = f.nativeElement.querySelectorAll('input[role="switch"]');
    expect(controls.length).toBe(4);
    expect(controls[1].checked).toBe(false);
    expect(f.nativeElement.textContent).not.toContain('Request sharing');
    controls[0].click(); await f.whenStable();
    expect(auth.submit).toHaveBeenCalledWith('privacy/request', { category: 'profile' });
    expect(auth.submit).toHaveBeenCalledWith('privacy/request', { category: 'presence' });
    f.destroy();
  });
  it('lets the owner stop sharing', async () => {
    http.get.mockReturnValue(of({ ...state, outgoing: [{ category: 'profile', status: 'accepted' }, { category: 'presence', status: 'accepted' }] }));
    const f = TestBed.createComponent(SharingComponent); await f.whenStable(); f.detectChanges();
    const control = f.nativeElement.querySelector('input');
    expect(control.checked).toBe(true);
    control.click(); await f.whenStable();
    expect(auth.submit).toHaveBeenCalledWith('privacy/cancel', { category: 'profile' });
    expect(auth.submit).toHaveBeenCalledWith('privacy/cancel', { category: 'presence' });
    f.destroy();
  });
  it('keeps book interests private by default and lets the owner share them', async () => {
    const f=TestBed.createComponent(SharingComponent);await f.whenStable();f.detectChanges();
    const control=f.nativeElement.querySelectorAll('input[role="switch"]')[2];
    expect(control.checked).toBe(false);
    control.click();await f.whenStable();
    expect(auth.submit).toHaveBeenCalledWith('privacy/request',{category:'books'});
    f.destroy();
  });
  it('restores the saved state if the request fails', async () => {
    auth.submit.mockRejectedValueOnce(new Error('offline'));
    const f = TestBed.createComponent(SharingComponent); await f.whenStable(); f.detectChanges();
    const control = f.nativeElement.querySelector('input');
    control.click(); await f.whenStable(); f.detectChanges();
    expect(control.checked).toBe(false);
    expect(f.nativeElement.querySelector('[role="alert"]').textContent).toContain('Unable to update sharing');
    f.destroy();
  });
  it('counts only shared picks and offers no approval controls', async () => {
    const f = TestBed.createComponent(SharingComponent); f.componentRef.setInput('notifications', true);
    await f.whenStable(); f.detectChanges();
    expect(TestBed.inject(SharingService).count()).toBe(2);
    expect(f.nativeElement.querySelector('input')).toBeNull();
    expect(f.nativeElement.textContent).not.toContain('Accept');
    expect(auth.submit).not.toHaveBeenCalled(); f.destroy();
  });
  it('disables preferences without a connection', async () => {
    http.get.mockReturnValue(of({ ...state, connected: false }));
    const f = TestBed.createComponent(SharingComponent); await f.whenStable(); f.detectChanges();
    for (const control of f.nativeElement.querySelectorAll('input')) expect(control.disabled).toBe(true);
    f.destroy();
  });
});
