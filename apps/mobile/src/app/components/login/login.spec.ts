import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { LoginComponent } from './login';
import { AuthService } from '../../auth.service';

describe('Account screens', () => {
  const auth = { me: vi.fn(), submit: vi.fn(), together: vi.fn() };
  beforeEach(async () => {
    vi.resetAllMocks();
    localStorage.clear();
    delete document.documentElement.dataset['theme'];
    window.history.replaceState(null, '', '/');
    auth.me.mockRejectedValue(new HttpErrorResponse({ status: 401 }));
    auth.together.mockResolvedValue({
      id: 'self',
      hasPhoto: false,
      invitationExpiresUtc: null,
      partner: null,
    });
    await TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [{ provide: AuthService, useValue: auth }],
    }).compileComponents();
  });
  async function render() {
    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }
  function click(fixture: Awaited<ReturnType<typeof render>>, text: string) {
    const button = [...fixture.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    button
      .find(
        (item) => item.textContent?.trim() === text || item.getAttribute('aria-label') === text,
      )!
      .click();
    fixture.detectChanges();
  }
  async function fill(fixture: Awaited<ReturnType<typeof render>>, values: Record<string, string>) {
    await fixture.whenStable();
    for (const [id, value] of Object.entries(values)) {
      const input = fixture.nativeElement.querySelector(`#${id}`);
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    await fixture.whenStable();
    fixture.detectChanges();
  }
  async function submitForm(fixture: Awaited<ReturnType<typeof render>>) {
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    fixture.detectChanges();
  }
  it('defaults to dark and restores an explicitly selected light theme', async () => {
    const fixture = await render();
    expect(document.documentElement.dataset['theme']).toBe('dark');
    click(fixture, 'Switch to light theme');
    expect(document.documentElement.dataset['theme']).toBe('light');
    expect(localStorage.getItem('overlap-theme')).toBe('light');
    fixture.destroy();
    await render();
    expect(document.documentElement.dataset['theme']).toBe('light');
  });
  it('registers with email and shows verification instructions without assuming eligibility', async () => {
    const fixture = await render();
    click(fixture, 'Create an account');
    await fill(fixture, {
      email: 'person@example.test',
      password: 'a sufficiently long password',
      'password-confirmation': 'a sufficiently long password',
    });
    auth.submit.mockResolvedValue({
      message: 'If this address is eligible, an email will arrive shortly.',
    });
    await submitForm(fixture);
    expect(auth.submit).toHaveBeenCalledWith('register', {
      email: 'person@example.test',
      password: 'a sufficiently long password',
      token: '',
    });
    expect(fixture.nativeElement.textContent).toContain('If this address is eligible');
    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    click(fixture, 'Request another link');
    expect(fixture.nativeElement.querySelector('#password')).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Request a fresh confirmation link');
  });
  it.each([
    ['Forgot password?', 'forgot-password'],
    ['Resend confirmation email', 'resend-confirmation'],
  ])('requests an email through %s', async (label, action) => {
    const fixture = await render();
    if (action === 'resend-confirmation') {
      expect(fixture.nativeElement.textContent).not.toContain('Resend confirmation email');
      await fill(fixture, { email: 'person@example.test', password: 'incorrect password' });
      auth.submit.mockRejectedValue(new HttpErrorResponse({ status: 401 }));
      await submitForm(fixture);
    }
    click(fixture, label);
    await fill(fixture, { email: 'person@example.test' });
    auth.submit.mockResolvedValue({ message: 'If eligible, check your inbox.' });
    await submitForm(fixture);
    expect(auth.submit).toHaveBeenCalledWith(action, {
      email: 'person@example.test',
      password: '',
      token: '',
    });
    expect(fixture.nativeElement.textContent).toContain('Your next step');
  });
  it('signs in and signs out through the API', async () => {
    const fixture = await render();
    await fill(fixture, { email: 'person@example.test', password: 'a sufficiently long password' });
    auth.submit.mockResolvedValue({ email: 'person@example.test', displayName: 'Person' });
    await submitForm(fixture);
    expect(fixture.nativeElement.textContent).toContain('Welcome, Person');
    expect(fixture.nativeElement.querySelector('app-dashboard')).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Welcome, Person');
    expect(fixture.nativeElement.querySelector('#password')).toBeNull();
    auth.submit.mockResolvedValue(undefined);
    click(fixture, 'Settings');
    click(fixture, 'Sign out');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(auth.submit).toHaveBeenLastCalledWith('logout', {});
    expect(fixture.nativeElement.querySelector('#password').value).toBe('');
  });
  it('changes the password with the current password and returns to sign in', async () => {
    auth.me.mockResolvedValue({ email: 'person@example.test', displayName: 'Person' });
    const fixture = await render();
    click(fixture, 'Settings');
    click(fixture, 'Change password');
    await fill(fixture, {
      'current-password': 'old password value',
      password: 'a sufficiently long password',
      'password-confirmation': 'a sufficiently long password',
    });
    auth.submit.mockResolvedValue({ message: 'Password changed. Sign in again.' });
    await submitForm(fixture);
    expect(auth.submit).toHaveBeenCalledWith('change-password', {
      currentPassword: 'old password value',
      newPassword: 'a sufficiently long password',
    });
    expect(fixture.nativeElement.textContent).toContain('Password changed');
    expect(fixture.nativeElement.querySelector('#password').value).toBe('');
  });
  it('resets the password using the email token', async () => {
    window.history.replaceState(
      null,
      '',
      '/#reset-password?email=person%40example.test&token=secret',
    );
    const fixture = await render();
    await fill(fixture, {
      password: 'a sufficiently long password',
      'password-confirmation': 'a sufficiently long password',
    });
    auth.submit.mockResolvedValue({ message: 'Password reset. Sign in again.' });
    await submitForm(fixture);
    expect(auth.submit).toHaveBeenCalledWith('reset-password', {
      email: 'person@example.test',
      token: 'secret',
      password: 'a sufficiently long password',
    });
    expect(fixture.nativeElement.textContent).toContain('Password reset');
  });
  it('blocks incomplete links and offers a new one', async () => {
    window.history.replaceState(null, '', '/#confirm-email?email=person%40example.test');
    const fixture = await render();
    await submitForm(fixture);
    expect(auth.submit).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('This link is incomplete');
    click(fixture, 'Link expired? Request a new one');
    expect(fixture.nativeElement.textContent).toContain('Request a fresh confirmation link');
  });
  it('rejects malformed email and hides cleared passwords after navigation', async () => {
    const fixture = await render();
    await fill(fixture, { email: 'invalid', password: 'a sufficiently long password' });
    await submitForm(fixture);
    expect(auth.submit).not.toHaveBeenCalled();
    click(fixture, 'Show passwords');
    expect(fixture.nativeElement.querySelector('#password').type).toBe('text');
    click(fixture, 'Create an account');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('#password').type).toBe('password');
    expect(fixture.nativeElement.querySelector('#password').value).toBe('');
  });
  it('shows login for an anonymous visitor', async () => {
    const fixture = await render();
    expect(
      fixture.nativeElement.querySelector('input[autocomplete="current-password"]'),
    ).toBeTruthy();
    expect(fixture.nativeElement.textContent).toContain('Create an account');
    expect(fixture.nativeElement.textContent).not.toContain('Resend confirmation email');
    expect(fixture.nativeElement.querySelector('[role="alert"]')).toBeNull();
  });
  it('requests email change from Settings with the current password', async () => {
    auth.me.mockResolvedValue({ email: 'person@example.test', displayName: 'Person' });
    const fixture = await render();
    click(fixture, 'Settings');
    click(fixture, 'Change email address');
    await fill(fixture, { 'new-email': 'new@example.test', 'current-password': 'abcdefghij' });
    auth.submit.mockResolvedValue({ message: 'If eligible, check your inbox.' });
    await submitForm(fixture);
    expect(auth.submit).toHaveBeenCalledWith('change-email', {
      newEmail: 'new@example.test',
      currentPassword: 'abcdefghij',
    });
    expect(fixture.nativeElement.textContent).toContain('Back to account');
  });
  it('requires explicit email-change confirmation and returns to login', async () => {
    window.history.replaceState(
      null,
      '',
      '/#confirm-email-change?email=old%40example.test&newEmail=new%40example.test&token=secret',
    );
    const fixture = await render();
    expect(window.location.hash).toBe('');
    expect(auth.submit).not.toHaveBeenCalled();
    auth.submit.mockResolvedValue({
      message: 'Email changed. Sign in with your new email address.',
    });
    await submitForm(fixture);
    expect(auth.submit).toHaveBeenCalledWith('confirm-email-change', {
      email: 'old@example.test',
      newEmail: 'new@example.test',
      token: 'secret',
    });
    expect(fixture.nativeElement.querySelector('#email').value).toBe('new@example.test');
  });
  it('asks for a saved name before showing the dashboard on a first session', async () => {
    auth.me.mockResolvedValue({ email: 'person@example.test', displayName: null });
    const fixture = await render();
    expect(fixture.nativeElement.textContent).toContain('What should we call you?');
    expect(fixture.nativeElement.querySelector('app-dashboard')).toBeNull();
    await fill(fixture, { 'display-name': 'My nickname' });
    auth.submit.mockResolvedValue({ email: 'person@example.test', displayName: 'My nickname' });
    await submitForm(fixture);
    expect(auth.submit).toHaveBeenCalledWith('profile', { displayName: 'My nickname' });
    expect(fixture.nativeElement.textContent).toContain('Welcome, My nickname');
    click(fixture, 'Profile');
    click(fixture, 'Edit display name');
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('#display-name').value).toBe('My nickname');
  });
  it('shows a recoverable service failure instead of a signed-in state', async () => {
    auth.me.mockRejectedValue(new HttpErrorResponse({ status: 503 }));
    const fixture = await render();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'can’t reach your account',
    );
    expect(fixture.nativeElement.textContent).not.toContain('Signed in as');
  });
  it('restores a valid session without storing credentials in the browser', async () => {
    auth.me.mockResolvedValue({ email: 'person@example.test', displayName: 'Person' });
    const fixture = await render();
    click(fixture, 'Settings');
    expect(fixture.nativeElement.textContent).not.toContain('30 minutes');
    expect(fixture.nativeElement.textContent).toContain('Change password');
    expect(fixture.nativeElement.querySelector('input[type="password"]')).toBeNull();
  });
  it('removes email tokens from the URL and requires explicit confirmation', async () => {
    window.history.replaceState(
      null,
      '',
      '/#confirm-email?email=person%40example.test&token=secret',
    );
    const fixture = await render();
    expect(window.location.hash).toBe('');
    expect(auth.submit).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain('Confirm my email');
    auth.submit.mockResolvedValue({ message: 'Email confirmed. You can now sign in.' });
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith('confirm-email', {
      email: 'person@example.test',
      token: 'secret',
      password: '',
    });
    expect(fixture.nativeElement.textContent).toContain('Email confirmed');
  });
  it('rejects mismatched passwords without making an account request', async () => {
    window.history.replaceState(
      null,
      '',
      '/#reset-password?email=person%40example.test&token=secret',
    );
    const fixture = await render();
    for (const [id, value] of [
      ['password', 'a sufficiently long password'],
      ['password-confirmation', 'a different long password'],
    ]) {
      const input = fixture.nativeElement.querySelector(`#${id}`);
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    fixture.nativeElement
      .querySelector('form')
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }));
    await fixture.whenStable();
    fixture.detectChanges();
    expect(auth.submit).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'don’t match',
    );
  });
  it('returns to Home from dashboard tabs and account forms through the brand', async () => {
    auth.me.mockResolvedValue({ email: 'person@example.test', displayName: 'Person' });
    const fixture = await render();
    for (const tab of ['Profile', 'Settings']) {
      click(fixture, tab);
      click(fixture, 'Overlap home');
      await fixture.whenStable();
      expect(
        fixture.nativeElement.querySelector('[aria-current="page"]').getAttribute('aria-label'),
      ).toBe('Home');
    }
    click(fixture, 'Settings');
    click(fixture, 'Change password');
    click(fixture, 'Overlap home');
    await fixture.whenStable();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('app-dashboard')).toBeTruthy();
    expect(fixture.nativeElement.querySelector('#password')).toBeNull();
  });
});
