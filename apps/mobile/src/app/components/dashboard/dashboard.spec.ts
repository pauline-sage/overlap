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
  it('opens a cooking reminder link after authentication and removes the query parameter',async()=>{
    history.replaceState(null,'','/?cookingContest=23');
    const fixture=await render();
    expect(fixture.componentInstance.cooking()).toBe(true);
    expect(fixture.componentInstance.cookingContestId()).toBe(23);
    expect(new URL(window.location.href).searchParams.has('cookingContest')).toBe(false);
    fixture.destroy();
  });
  it('opens shared time and returns home through dashboard navigation', async () => {
    const fixture = await render();
    fixture.nativeElement.querySelector('button.time-idea').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#dashboard-heading').textContent).toContain('Stay in together');
    fixture.nativeElement.querySelector('button.watch-choice').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#dashboard-heading').textContent).toContain(
      'Watch together',
    );
    fixture.componentInstance.navigate('home');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#dashboard-heading').textContent).toContain(
      'Close, wherever you are.',
    );
    expect(fixture.nativeElement.textContent).not.toContain('Watch together');
  });
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
    fixture.componentInstance.navigate('profile');
    fixture.detectChanges();
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
    fixture.componentInstance.navigate('profile');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#dashboard-heading').textContent).toBe(
      "Me's profile",
    );
    expect(fixture.nativeElement.querySelector('[aria-label="Add or replace photo"]')).toBeTruthy();
    fixture.nativeElement.querySelector('[aria-label="Remove my photo"]').click();
    await fixture.whenStable();
    fixture.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith('together/photo', { pixels: null });
    expect(fixture.nativeElement.querySelector('img')).toBeNull();
    expect(fixture.nativeElement.querySelector('[aria-label="Remove my photo"]')).toBeNull();
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
    expect(fixture.nativeElement.textContent).toContain('Phone notifications');
    expect(fixture.nativeElement.textContent).not.toContain('Close, wherever you are.');
    fixture.nativeElement.querySelector('button[aria-label="Profile"]').click();
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Edit display name');
    expect(
      fixture.nativeElement.querySelector('[aria-current="page"]').getAttribute('aria-label'),
    ).toBe('Profile');
  });
  it.each([
    { invitationExpiresUtc: '2099-01-01T00:00:00Z', partner: null },
    {
      invitationExpiresUtc: null,
      partner: { id: 'partner', displayName: 'Partner', hasPhoto: false },
    },
  ])(
    'does not offer another invitation for an existing invitation or connection: %j',
    async (state) => {
      auth.together.mockResolvedValue({ ...status, ...state });
      const fixture = await render();
      expect(fixture.nativeElement.querySelector('[aria-label="Invite your person"]')).toBeNull();
      fixture.componentInstance.navigate('profile');
      fixture.detectChanges();
      expect(fixture.nativeElement.textContent).not.toContain('Invite your person');
      await fixture.componentInstance.createInvite();
      expect(auth.submit).not.toHaveBeenCalled();
    },
  );
  it('refreshes server state before creating an invitation to avoid replacing a pending link', async () => {
    const fixture = await render();
    auth.together.mockResolvedValue({ ...status, invitationExpiresUtc: '2099-01-01T00:00:00Z' });
    await fixture.componentInstance.createInvite();
    expect(auth.submit).not.toHaveBeenCalled();
    expect(fixture.componentInstance.page()).toBe('profile');
  });
  it('offers invitations again after revocation and clears the old link', async () => {
    auth.together.mockResolvedValue({ ...status, invitationExpiresUtc: '2099-01-01T00:00:00Z' });
    const fixture = await render();
    fixture.componentInstance.inviteUrl.set('old-link');
    auth.together.mockResolvedValue(status);
    await fixture.componentInstance.revoke();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[aria-label="Invite your person"]')).toBeTruthy();
    expect(fixture.componentInstance.inviteUrl()).toBe('');
  });
  it('clears a stale link on refresh when the invitation expires or is accepted', async () => {
    const fixture = await render();
    fixture.componentInstance.inviteUrl.set('old-link');
    await fixture.componentInstance.refresh();
    expect(fixture.componentInstance.inviteUrl()).toBe('');
    expect(fixture.componentInstance.canInvite()).toBe(true);
  });
  it('keeps an incoming invitation focused on acceptance rather than another invite', async () => {
    auth.submit.mockResolvedValue({ displayName: 'Partner', expiresUtc: '2099-01-01T00:00:00Z' });
    const fixture = await render('private');
    expect(fixture.nativeElement.querySelector('[aria-label="Invite your person"]')).toBeNull();
    await fixture.componentInstance.createInvite();
    expect(auth.submit).toHaveBeenCalledTimes(1);
    fixture.componentInstance.decline();
    fixture.componentInstance.navigate('home');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[aria-label="Invite your person"]')).toBeTruthy();
  });
  it('refreshes the photo URL even when the partner still has a photo', async () => {
    const visibility=vi.spyOn(document,'visibilityState','get').mockReturnValue('visible');
    try {
      auth.together.mockResolvedValue({...status,partner:{id:'partner',displayName:'Juu',hasPhoto:true,online:true}});
      const f=await render(); const c=f.componentInstance;
      const previous=c.photoUrl('partner');
      await c.checkPresence(); f.detectChanges();
      expect(c.photoUrl('partner')).not.toBe(previous);
      expect(f.nativeElement.querySelector('.partner img').getAttribute('src')).toBe(c.photoUrl('partner'));
      c.navigate('profile'); f.detectChanges();
      expect(f.nativeElement.querySelector('.partner-row img').getAttribute('src')).toBe(c.photoUrl('partner'));
      f.destroy();
    } finally { visibility.mockRestore(); }
  });

});
