import { TestBed } from '@angular/core/testing';
import { IdleSessionService } from './idle-session.service';
import { AuthService } from './auth.service';
import { HttpErrorResponse } from '@angular/common/http';

describe('Inactivity session', () => {
  let service: IdleSessionService;
  const auth = { submit: vi.fn() };
  beforeEach(() => {
    vi.useFakeTimers();
    localStorage.clear();
    auth.submit.mockReset().mockResolvedValue(undefined);
    TestBed.configureTestingModule({
      providers: [IdleSessionService, { provide: AuthService, useValue: auth }],
    });
    service = TestBed.inject(IdleSessionService);
  });
  afterEach(() => {
    service.stop();
    vi.useRealTimers();
  });
  it('locks after 30 idle minutes without sending background renewals', async () => {
    const ended = vi.fn();
    service.start(ended, true);
    await vi.advanceTimersByTimeAsync(29 * 60_000);
    expect(ended).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(60_000);
    expect(ended).toHaveBeenCalledOnce();
    expect(auth.submit).toHaveBeenCalledExactlyOnceWith('logout', {});
  });
  it('extends the local deadline on interaction and renews active sessions', async () => {
    const ended = vi.fn();
    service.start(ended, true);
    await vi.advanceTimersByTimeAsync(20 * 60_000);
    window.dispatchEvent(new Event('pointerdown'));
    await vi.advanceTimersByTimeAsync(60_000);
    expect(auth.submit).toHaveBeenCalledWith('activity', {});
    await vi.advanceTimersByTimeAsync(20 * 60_000);
    expect(ended).not.toHaveBeenCalled();
    await vi.advanceTimersByTimeAsync(9 * 60_000);
    expect(ended).toHaveBeenCalledOnce();
  });
  it('does not let a late click resurrect an idle session', () => {
    const ended = vi.fn();
    service.start(ended, true);
    vi.setSystemTime(Date.now() + 31 * 60_000);
    window.dispatchEvent(new Event('pointerdown'));
    expect(ended).toHaveBeenCalledOnce();
  });
  it('locks immediately on an activity endpoint 401', async () => {
    const ended = vi.fn();
    service.start(ended, true);
    auth.submit.mockRejectedValue(new HttpErrorResponse({ status: 401 }));
    await vi.advanceTimersByTimeAsync(1000);
    window.dispatchEvent(new Event('keydown'));
    await vi.advanceTimersByTimeAsync(60_000);
    expect(ended).toHaveBeenCalledOnce();
  });
  it('shares logout across tabs', () => {
    const ended = vi.fn();
    service.start(ended, true);
    window.dispatchEvent(
      new StorageEvent('storage', { key: 'overlap-last-activity', newValue: '0' }),
    );
    expect(ended).toHaveBeenCalledOnce();
  });
});
