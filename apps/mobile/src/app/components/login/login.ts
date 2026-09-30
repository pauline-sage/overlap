import { LoadingIndicatorComponent } from '../loading-indicator';
import { PrivacyNoticeComponent } from '../privacy-notice/privacy-notice';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, inject, signal, viewChild } from '@angular/core';
import { IdleSessionService } from '../../idle-session.service';
import { FormsModule } from '@angular/forms';
import { Account, AuthMessage, AuthService } from '../../auth.service';
import { DashboardComponent } from '../dashboard/dashboard';

type Screen =
  | 'change-email'
  | 'confirm-email-change'
  | 'profile'
  | 'login'
  | 'register'
  | 'forgot-password'
  | 'resend-confirmation'
  | 'confirm-email'
  | 'reset-password'
  | 'account'
  | 'change-password';

@Component({
  selector: 'app-login',
  imports: [LoadingIndicatorComponent,PrivacyNoticeComponent, FormsModule, DashboardComponent],
  templateUrl: './login.html',
  styleUrl: './login.scss',
})
export class LoginComponent {
  private readonly auth = inject(AuthService);
  private readonly dashboard = viewChild(DashboardComponent);
  private readonly idle = inject(IdleSessionService);
  protected readonly screen = signal<Screen>('login');
  protected readonly account = signal<Account | null>(null);
  protected readonly busy = signal(false);
  protected readonly loading = signal(true);
  protected readonly message = signal('');
  protected readonly error = signal('');
  protected readonly theme = signal<'dark' | 'light'>('dark');
  protected readonly revealPassword = signal(false);
  protected readonly emailSent = signal(false);
  protected readonly showConfirmationHelp = signal(false);
  protected email = '';
  protected newEmail = '';
  protected displayName = '';
  protected password = '';
  protected currentPassword = '';
  protected passwordConfirmation = '';
  private token = '';
  protected invitationToken = '';
  private readonly invitationKey = 'overlap-pending-invitation';
  protected clearInvitation(): void {
    this.invitationToken = '';
    try { localStorage.removeItem(this.invitationKey); } catch { /* Storage may be disabled. */ }
  }
  private restoreInvitation(): void {
    try {
      const saved = JSON.parse(localStorage.getItem(this.invitationKey) ?? 'null');
      if (saved && typeof saved.token === 'string' && saved.token.length <= 256 && Number.isFinite(saved.expires) && saved.expires > Date.now()) this.invitationToken = saved.token;
      else this.clearInvitation();
    } catch { this.clearInvitation(); }
  }
  protected readonly headings: Record<Screen, string> = {
    'change-email': 'A new address.',
    'confirm-email-change': 'Confirm your new email.',
    profile: 'What should we call you?',
    login: 'Welcome back.',
    register: 'Make time together.',
    'forgot-password': 'Let’s get you back in.',
    'resend-confirmation': 'Check your inbox.',
    'confirm-email': 'One last step.',
    'reset-password': 'A fresh start.',
    account: 'Your account',
    'change-password': 'Keep it yours.',
  };
  constructor() {
    inject(DestroyRef).onDestroy(() => this.idle.stop());
    try {
      if (localStorage.getItem('overlap-theme') === 'light') this.theme.set('light');
    } catch {
      /* Storage may be disabled; the theme still works for this visit. */
    }
    document.documentElement.dataset['theme'] = this.theme();
    this.restoreInvitation();
    const fragment = window.location.hash.slice(1);
    const separator = fragment.indexOf('?');
    const action = fragment.slice(0, separator);
    if (separator >= 0 && action === 'invite') {
      this.invitationToken = new URLSearchParams(fragment.slice(separator + 1)).get('token') ?? '';
      if (this.invitationToken.length <= 256 && this.invitationToken) {
        try { localStorage.setItem(this.invitationKey, JSON.stringify({token:this.invitationToken,expires:Date.now()+48*60*60*1000})); } catch { /* Keep the in-memory invitation. */ }
      } else this.clearInvitation();
      window.history.replaceState(null, '', window.location.pathname);
    }
    if (
      separator >= 0 &&
      (action === 'confirm-email' ||
        action === 'reset-password' ||
        action === 'confirm-email-change')
    ) {
      const parameters = new URLSearchParams(fragment.slice(separator + 1));
      this.email = parameters.get('email') ?? '';
      this.token = parameters.get('token') ?? '';
      this.newEmail = parameters.get('newEmail') ?? '';
      this.screen.set(action);
      if (!this.email || !this.token || (action === 'confirm-email-change' && !this.newEmail))
        this.error.set('This link is incomplete. Request a new email link below.');
      window.history.replaceState(null, '', window.location.pathname);
      this.loading.set(false);
    } else {
      void this.loadSession();
    }
  }
  private async loadSession(): Promise<void> {
    try {
      this.account.set(await this.auth.me());
      this.screen.set(this.account()?.displayName ? 'account' : 'profile');
      this.idle.start(() => this.sessionEnded());
    } catch (error) {
      if (!(error instanceof HttpErrorResponse && error.status === 401)) this.showError(error);
    } finally {
      this.loading.set(false);
    }
  }
  protected navigate(screen: Screen): void {
    if (this.busy()) return;
    if (screen === 'account' && !this.account()?.displayName) screen = 'profile';
    if (screen === 'profile') this.displayName = this.account()?.displayName ?? '';
    this.screen.set(screen);
    this.emailSent.set(false);
    this.showConfirmationHelp.set(false);
    this.revealPassword.set(false);
    this.token = '';
    this.message.set('');
    this.error.set('');
    this.password = this.currentPassword = this.passwordConfirmation = '';
    if (screen === 'change-email' || screen === 'login') this.newEmail = '';
    queueMicrotask(() =>
      document.getElementById(screen === 'account' ? 'dashboard-heading' : 'page-title')?.focus(),
    );
  }
  protected goHome(): void {
    if (this.busy() || this.loading()) return;
    if (this.account() && this.screen() === 'account') {
      this.dashboard()?.navigate('home');
    } else {
      this.navigate(this.account() ? 'account' : 'login');
    }
  }
  protected toggleTheme(): void {
    this.theme.update((value) => (value === 'dark' ? 'light' : 'dark'));
    document.documentElement.dataset['theme'] = this.theme();
    try {
      localStorage.setItem('overlap-theme', this.theme());
    } catch {
      /* Optional preference only. */
    }
  }
  protected hasLink(): boolean {
    return !!this.token && !!this.email;
  }
  protected async submit(valid = true): Promise<void> {
    if (this.busy()) return;
    if (!valid) {
      this.error.set('Please complete the fields with a valid email and password.');
      return;
    }
    const screen = this.screen();
    this.showConfirmationHelp.set(false);
    this.error.set('');
    this.message.set('');
    if (
      ['confirm-email', 'reset-password', 'confirm-email-change'].includes(screen) &&
      !this.hasLink()
    ) {
      this.error.set('This link is incomplete. Request a new email link below.');
      return;
    }
    if (
      ['register', 'reset-password', 'change-password'].includes(screen) &&
      this.password !== this.passwordConfirmation
    ) {
      this.error.set('Your passwords don’t match. Please check them.');
      return;
    }
    this.busy.set(true);
    const accountAtStart = this.account();
    try {
      const body =
        screen === 'change-email'
          ? { newEmail: this.newEmail.trim(), currentPassword: this.currentPassword }
          : screen === 'confirm-email-change'
            ? { email: this.email, newEmail: this.newEmail, token: this.token }
            : screen === 'profile'
              ? { displayName: this.displayName.trim() }
              : screen === 'change-password'
                ? { currentPassword: this.currentPassword, newPassword: this.password }
                : { email: this.email.trim(), password: this.password, token: this.token };
      const result = await this.auth.submit<Account | AuthMessage>(screen, body);
      if (accountAtStart && !this.account()) return;
      this.busy.set(false);
      if (screen === 'login' || screen === 'profile') {
        this.account.set(result as Account);
        this.navigate('account');
        if (screen === 'login') this.idle.start(() => this.sessionEnded(), true);
      } else if (
        ['reset-password', 'change-password', 'confirm-email', 'confirm-email-change'].includes(
          screen,
        )
      ) {
        if (screen === 'confirm-email-change') {
          this.email = this.newEmail;
          this.idle.broadcastLogout();
        }
        this.token = '';
        this.account.set(null);
        this.idle.stop();
        this.navigate('login');
        this.message.set((result as AuthMessage).message);
        if (screen === 'confirm-email') {
          this.message.set('Email confirmed! Sign in below to open your dashboard.');
        }
      } else {
        this.emailSent.set(true);
        this.message.set((result as AuthMessage).message);
      }
    } catch (error) {
      this.showConfirmationHelp.set(
        screen === 'login' && error instanceof HttpErrorResponse && error.status === 401,
      );
      this.showError(error);
    } finally {
      this.password = this.currentPassword = this.passwordConfirmation = '';
      this.revealPassword.set(false);
      this.busy.set(false);
    }
  }
  protected async logout(): Promise<void> {
    this.busy.set(true);
    try {
      await this.auth.submit<void>('logout', {});
      this.account.set(null);
      this.idle.broadcastLogout();
      this.idle.stop();
      this.busy.set(false);
      this.navigate('login');
    } catch (error) {
      this.showError(error);
    } finally {
      this.busy.set(false);
    }
  }
  private showError(error: unknown): void {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 429) {
        this.error.set(
          'Too many attempts. Please wait before trying again. Email requests may require ten minutes.',
        );
        return;
      }
      if (error.status === 0 || error.status >= 500) {
        this.error.set('We can’t reach your account right now. Please try again shortly.');
        return;
      }
      if (error.status === 401 && this.account()) {
        this.idle.stop();
        this.account.set(null);
        this.busy.set(false);
        this.navigate('login');
        this.error.set('Your session has ended. Please sign in again.');
        return;
      }
      this.error.set(error.error?.title ?? 'Something went wrong. Please try again.');
    } else {
      this.error.set('Something went wrong. Please try again.');
    }
  }
  protected sessionEnded(): void {
    this.idle.stop();
    this.account.set(null);
    this.busy.set(false);
    this.navigate('login');
    this.message.set('Your session ended. Sign in again to continue.');
  }
}
