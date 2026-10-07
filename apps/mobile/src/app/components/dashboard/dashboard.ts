import { StayInTogetherComponent } from '../stay-in-together/stay-in-together';
import { ScheduleComponent } from '../schedule/schedule';
import { StayInRequest } from '../stay-in-together/stay-in.types';
import { LoadingIndicatorComponent } from '../loading-indicator/loading-indicator';

import { OutingUpdate, SharingService } from '../nav-bar/sharing/sharing.service';
import { StepOutTogetherComponent } from '../step-out-together/step-out-together';
import { ProfileComponent, ProfileAction } from '../profile/profile';
import { Component, inject, input, output, signal, OnInit, OnDestroy, NgZone } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService, TogetherStatus } from '../../auth.service';
import { DashboardPage, NavBarComponent } from '../nav-bar/nav-bar';

@Component({
  selector: 'app-dashboard',
  providers: [SharingService],
  imports: [ScheduleComponent, StayInTogetherComponent, LoadingIndicatorComponent, NavBarComponent, ProfileComponent, StepOutTogetherComponent],
  templateUrl: './dashboard.html',
  styleUrl: './dashboard.scss',
})
export class DashboardComponent implements OnInit, OnDestroy {
  readonly scheduleOpened=signal(false);
  openSchedule():void {this.navigate('home');this.scheduleOpened.set(true);queueMicrotask(()=>document.getElementById('dashboard-heading')?.focus());}
  readonly sharing = inject(SharingService);
  openPartnerPicks():void {this.openActivity({activity:'watch',partnerPicks:true});}
  readonly firstSteps = signal(false);
  finishFirstSteps(settings=false):void {
    this.firstSteps.set(false);
    if(settings) this.navigate('settings');
  }
  private welcomeConnection():void {
    this.firstSteps.set(true);
    void this.sharing.load(true);
    queueMicrotask(()=>document.getElementById('first-steps-heading')?.focus());
  }
  readonly coffee=signal(false);
  readonly stayingIn=signal(false);
  readonly stayInRequest=signal<StayInRequest>({activity:'menu'});
  private openActivity(request:StayInRequest):void{this.navigate('home');this.stayInRequest.set(request);this.stayingIn.set(true);queueMicrotask(()=>document.getElementById('dashboard-heading')?.focus());}
  openStayIn():void{this.openActivity({activity:'menu'});}
  openQuiz(id=0):void{this.openActivity({activity:'quiz',entryId:id});}
  openCooking(entryId=0):void{this.openActivity({activity:'cooking',entryId});}
  openContest(id:number):void{this.openActivity({activity:'cooking',contestId:id});}
  openReading():void{this.openActivity({activity:'read'});}
  openSharedTime():void{this.openActivity({activity:'watch'});}
  private openReminderLink():void{
    const url=new URL(window.location.href);const value=url.searchParams.get('cookingContest');
    if(url.searchParams.get('schedule')==='1'){this.openSchedule();url.searchParams.delete('schedule');history.replaceState(null,'',url.pathname+url.search+url.hash);return;}
    if(value && /^\d+$/.test(value) && Number.isSafeInteger(Number(value)) && Number(value)>0){this.openContest(Number(value));url.searchParams.delete('cookingContest');history.replaceState(null,'',url.pathname+url.search+url.hash);}
  }
  readonly outingProposal=signal<OutingUpdate|null>(null);
  pickYours(proposal:OutingUpdate):void{this.navigate('home');this.outingProposal.set(proposal);this.coffee.set(true);}
  openCoffee():void{this.navigate('home');this.outingProposal.set(null);this.coffee.set(true);}
  readonly page=signal<DashboardPage>('home');
  readonly sessionExpired=output<void>();
  navigate(page:DashboardPage):void{this.scheduleOpened.set(false);this.stayingIn.set(false);this.coffee.set(false);this.page.set(page);queueMicrotask(()=>document.getElementById('dashboard-heading')?.focus());}
  private readonly auth = inject(AuthService);
  readonly invitationToken = input('');
  readonly invitationHandled = output<void>();
  readonly together = signal<TogetherStatus | null>(null);
  readonly working = signal(false);
  readonly notice = signal('');
  readonly error = signal('');
  readonly inviteUrl = signal('');
  readonly invitation = signal<{ displayName: string; expiresUtc: string } | null>(null);
  readonly photoVersion = signal(Date.now());
  readonly showLeave = signal(false);
  readonly presenceKnown = signal(false);
  private readonly zone = inject(NgZone);
  private presenceTimer?: ReturnType<typeof setInterval>;
  private checkingPresence = false;
  private destroyed = false;
  private readonly onVisibility = () => {
    this.presenceKnown.set(false);
    if (document.visibilityState === 'visible') void this.checkPresence();
  };
  async checkPresence(): Promise<void> {
    if (
      this.checkingPresence ||
      this.working() ||
      this.destroyed ||
      document.visibilityState !== 'visible'
    )
      return;
    this.checkingPresence = true;
    try {
      const status = await this.auth.together();
      if (!this.destroyed) {
        const justConnected=!!this.together() && !this.together()?.partner && !!status.partner;
        this.together.set(status);
        if(justConnected) this.welcomeConnection();
        this.photoVersion.update((version) => version + 1);
        this.presenceKnown.set(true);
      }
    } catch (error) {
      this.presenceKnown.set(false);
      if (!this.destroyed && error instanceof HttpErrorResponse && error.status === 401)
        this.sessionExpired.emit();
    } finally {
      this.checkingPresence = false;
    }
  }
  presenceLabel(online: boolean | null | undefined): string {
    return !this.presenceKnown() || online == null
      ? 'Status unavailable'
      : online
        ? 'Online'
        : 'Away';
  }
  ngOnDestroy(): void {
    this.destroyed = true;
    clearInterval(this.presenceTimer);
    document.removeEventListener('visibilitychange', this.onVisibility);
  }
  async ngOnInit(): Promise<void> {
    this.openReminderLink();
    await this.run(async () => {
      this.together.set(await this.auth.together());
      this.presenceKnown.set(true);
      if (this.invitationToken()) {
        if (this.together()?.partner) { this.invitationHandled.emit(); return; }
        this.navigate('profile');
        try {
          this.invitation.set(await this.auth.submit('together/preview', { token: this.invitationToken() }));
        } catch (error) {
          if (error instanceof HttpErrorResponse && error.status === 400) this.invitationHandled.emit();
          throw error;
        }
      }
    });
    if (this.destroyed) return;
    this.zone.runOutsideAngular(() => {
      this.presenceTimer = setInterval(() => void this.checkPresence(), 30000);
      document.addEventListener('visibilitychange', this.onVisibility);
    });
  }
  profileAction(action: ProfileAction): void {
    if (action === 'editProfile') this.editProfile.emit();
    else void this[action]();
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
      if (this.together()?.partner || !this.together()?.invitationExpiresUtc)
        this.inviteUrl.set('');
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
      this.navigate('home');
      this.welcomeConnection();
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
