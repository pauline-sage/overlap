import { TestBed } from '@angular/core/testing';
import { DashboardComponent } from './dashboard';
import { AuthService } from '../../auth.service';

describe('Dashboard photos and invitations', () => {
  const auth = { together: vi.fn(), submit: vi.fn() };
  const status = { id: 'self', hasPhoto: false, invitationExpiresUtc: null, partner: null };
  beforeEach(async () => {
    vi.resetAllMocks();
    auth.together.mockResolvedValue(status);
    await TestBed.configureTestingModule({
      imports: [DashboardComponent],
      providers: [{ provide: AuthService, useValue: auth }],
    }).compileComponents();
  });
  async function render(token = '') {
    const fixture = TestBed.createComponent(DashboardComponent);
    fixture.componentRef.setInput('email', 'me@example.test');
    fixture.componentRef.setInput('greetingName', 'Me');
    fixture.componentRef.setInput('invitationToken', token);
    fixture.detectChanges();
    await fixture.whenStable();
    await vi.waitFor(() => expect(fixture.componentInstance.working()).toBe(false));
    fixture.detectChanges();
    return fixture;
  }
  it('generates a link only after an explicit request and can revoke it', async () => {
    const fixture = await render();
    expect(auth.submit).not.toHaveBeenCalled();
    auth.submit.mockResolvedValue({ url: 'http://localhost/#invite?token=private' });
    await fixture.componentInstance.createInvite();
    fixture.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith('together/invite', {});
    expect(fixture.nativeElement.querySelector('#invitation-link').value).toContain(
      'token=private',
    );
    await fixture.componentInstance.revoke();
    fixture.detectChanges();
    expect(auth.submit).toHaveBeenLastCalledWith('together/revoke', {});
    expect(fixture.nativeElement.querySelector('#invitation-link')).toBeNull();
  });
  it('previews an invitation without automatically accepting it', async () => {
    auth.submit.mockResolvedValue({ displayName: 'Partner', expiresUtc: '2026-09-25T12:00:00Z' });
    const fixture = await render('private');
    expect(auth.submit).toHaveBeenCalledTimes(1);
    expect(auth.submit).toHaveBeenCalledWith('together/preview', { token: 'private' });
    expect(fixture.nativeElement.textContent).toContain('Partner invited you');
    await fixture.componentInstance.accept();
    fixture.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith('together/accept', { token: 'private' });
    expect(fixture.nativeElement.textContent).not.toContain('Partner invited you');
  });
  it('removes the current photo and restores the initial fallback', async () => {
    auth.together.mockResolvedValueOnce({ ...status, hasPhoto: true });
    const fixture = await render();
    expect(fixture.nativeElement.querySelector('img').src).toContain(
      '/api/auth/together/photo/self',
    );
    await fixture.componentInstance.removePhoto();
    fixture.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith('together/photo', { pixels: null });
    expect(fixture.nativeElement.querySelector('img')).toBeNull();
  });
  it('rejects SVG files without uploading them', async () => {
    const fixture = await render();
    await fixture.componentInstance.uploadPhoto({
      target: { files: [new File(['<svg/>'], 'photo.svg', { type: 'image/svg+xml' })], value: '' },
    } as unknown as Event);
    fixture.detectChanges();
    expect(auth.submit).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'JPEG, PNG or WebP',
    );
  });
  it('keeps account controls out of Home and exposes them through navigation', async () => {
    const fixture = await render();
    expect(fixture.nativeElement.textContent).not.toContain('Change password');
    fixture.nativeElement.querySelector('button[aria-label="Settings"]').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Change password');
    expect(fixture.nativeElement.textContent).toContain('Sign out');
    expect(fixture.nativeElement.textContent).not.toContain('A little closer.');
    fixture.nativeElement.querySelector('button[aria-label="Profile"]').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Edit display name');
    expect(
      fixture.nativeElement.querySelector('[aria-current="page"]').getAttribute('aria-label'),
    ).toBe('Profile');
  });
});
