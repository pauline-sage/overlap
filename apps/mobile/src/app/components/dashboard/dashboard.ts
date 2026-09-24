import { Component, inject, input, output, signal, OnInit } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService, TogetherStatus } from '../../auth.service';
import { DashboardPage, NavBarComponent } from '../nav-bar/nav-bar';

@Component({
  selector: 'app-dashboard',
  imports: [NavBarComponent],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class DashboardComponent implements OnInit {
  readonly page = signal<DashboardPage>('home');
  readonly sessionExpired = output<void>();
  readonly theme = input<'light' | 'dark'>('dark');
  readonly toggleTheme = output<void>();
  navigate(page: DashboardPage): void {
    this.page.set(page);
    queueMicrotask(() => document.getElementById('dashboard-heading')?.focus());
  }
  private readonly auth = inject(AuthService);
  readonly invitationToken = input('');
  readonly invitationHandled = output<void>();
  readonly together = signal<TogetherStatus | null>(null);
  readonly working = signal(false);
  readonly notice = signal('');
  readonly error = signal('');
  readonly inviteUrl = signal('');
  readonly invitation = signal<{ displayName: string; expiresUtc: string } | null>(null);
  readonly photoVersion = signal(0);
  readonly showLeave = signal(false);
  async ngOnInit(): Promise<void> {
    await this.run(async () => {
      this.together.set(await this.auth.together());
      if (this.invitationToken())
        this.invitation.set(
          await this.auth.submit('together/preview', { token: this.invitationToken() }),
        );
    });
  }
  photoUrl(id: string): string {
    return `/api/auth/together/photo/${encodeURIComponent(id)}?v=${this.photoVersion()}`;
  }
  private async run(action: () => Promise<void>): Promise<void> {
    if (this.working()) return;
    this.working.set(true);
    this.error.set('');
    this.notice.set('');
    try {
      await action();
    } catch (error) {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        this.sessionExpired.emit();
        return;
      }
      this.error.set(
        error instanceof HttpErrorResponse
          ? (error.error?.title ?? 'Unable to complete this action. Please try again.')
          : 'Unable to complete this action. Please try again.',
      );
    } finally {
      this.working.set(false);
    }
  }
  async refresh(): Promise<void> {
    await this.run(async () => {
      this.together.set(await this.auth.together());
      if (this.together()?.partner || !this.together()?.invitationExpiresUtc) this.inviteUrl.set('');
      this.photoVersion.update((v) => v + 1);
    });
  }
  canInvite(): boolean {
    const status = this.together();
    return !!status && !status.partner && !status.invitationExpiresUtc && !this.invitation();
  }
  async createInvite(): Promise<void> {
    if (!this.canInvite()) return;
    await this.run(async () => {
      this.together.set(await this.auth.together());
      if (!this.canInvite()) {
        this.inviteUrl.set('');
        this.navigate('profile');
        return;
      }
      const result = await this.auth.submit<{ url: string }>('together/invite', {});
      this.inviteUrl.set(result.url);
      this.page.set('profile');
      this.together.set(await this.auth.together());
      this.notice.set(
        'Your private link is ready. It expires in 48 hours. Share it only with your person.',
      );
    });
  }
  async copyInvite(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.inviteUrl());
      this.notice.set('Invitation copied.');
    } catch {
      this.notice.set('Select the link below and copy it manually.');
    }
  }
  async revoke(): Promise<void> {
    await this.run(async () => {
      await this.auth.submit('together/revoke', {});
      this.inviteUrl.set('');
      this.together.set(await this.auth.together());
      this.notice.set('Invitation revoked.');
    });
  }
  decline(): void {
    this.invitation.set(null);
    this.invitationHandled.emit();
  }
  async accept(): Promise<void> {
    await this.run(async () => {
      await this.auth.submit('together/accept', { token: this.invitationToken() });
      this.decline();
      this.inviteUrl.set('');
      this.together.set(await this.auth.together());
      this.notice.set('You’re connected!');
    });
  }
  async leave(): Promise<void> {
    await this.run(async () => {
      await this.auth.submit('together/leave', {});
      this.showLeave.set(false);
      this.together.set(await this.auth.together());
      this.notice.set('Connection ended for both of you.');
    });
  }
  async removePhoto(): Promise<void> {
    await this.run(async () => {
      await this.auth.submit('together/photo', { pixels: null });
      this.photoVersion.update((v) => v + 1);
      this.together.set(await this.auth.together());
    });
  }
  async uploadPhoto(event: Event): Promise<void> {
    const control = event.target as HTMLInputElement;
    const file = control.files?.[0];
    control.value = '';
    if (!file) return;
    if (
      !['image/jpeg', 'image/png', 'image/webp'].includes(file.type) ||
      file.size > 5 * 1024 * 1024
    ) {
      this.error.set('Choose a JPEG, PNG or WebP photo smaller than 5 MB.');
      return;
    }
    await this.run(async () => {
      const bitmap = await createImageBitmap(file);
      try {
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = 128;
        const context = canvas.getContext('2d');
        if (!context) throw new Error('Canvas unavailable');
        context.fillStyle = '#d5eab5';
        context.fillRect(0, 0, 128, 128);
        const size = Math.min(bitmap.width, bitmap.height);
        context.drawImage(
          bitmap,
          (bitmap.width - size) / 2,
          (bitmap.height - size) / 2,
          size,
          size,
          0,
          0,
          128,
          128,
        );
        const rgba = context.getImageData(0, 0, 128, 128).data;
        const rgb = new Uint8Array(128 * 128 * 3);
        for (let i = 0, j = 0; i < rgba.length; i += 4) {
          rgb[j++] = rgba[i];
          rgb[j++] = rgba[i + 1];
          rgb[j++] = rgba[i + 2];
        }
        const pixels = btoa(Array.from(rgb, (value) => String.fromCharCode(value)).join(''));
        await this.auth.submit('together/photo', { pixels });
        this.photoVersion.update((v) => v + 1);
        this.together.set(await this.auth.together());
        this.notice.set('Photo updated.');
      } finally {
        bitmap.close();
      }
    });
  }
  readonly email = input.required<string>();
  readonly greetingName = input.required<string>();
  readonly editProfile = output<void>();
  readonly busy = input(false);
  readonly changePassword = output<void>();
  readonly changeEmail = output<void>();
  readonly signOut = output<void>();
}
