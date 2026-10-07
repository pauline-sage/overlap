import { Component, input, output, signal, effect } from '@angular/core';

import { TogetherStatus } from '../../auth.service';
import { PartnerSharesComponent } from './partner-shares/partner-shares';
import { ProfileTimezoneComponent } from './profile-timezone';
export type ProfileAction =
  | 'removePhoto'
  | 'refresh'
  | 'createInvite'
  | 'accept'
  | 'decline'
  | 'copyInvite'
  | 'revoke'
  | 'leave'
  | 'editProfile';
@Component({ selector: 'app-profile', imports:[PartnerSharesComponent,ProfileTimezoneComponent], templateUrl: './profile.html', styleUrl: './profile.scss' })
export class ProfileComponent {
  readonly openRecipe=output<number>();
  readonly openContest=output<number>();
  readonly openQuiz=output<number>();
  readonly greetingName = input.required<string>();
  readonly together = input<TogetherStatus | null>(null);
  readonly busy = input(false);
  readonly working = input(false);
  readonly notice = input('');
  readonly error = input('');
  readonly inviteUrl = input('');
  readonly invitation = input<{ displayName: string; expiresUtc: string } | null>(null);
  readonly photoVersion = input(0);
  readonly action = output<ProfileAction>();
  readonly upload = output<Event>();
  readonly showLeave = signal(false);
  readonly viewingPartner = signal(false);
  private readonly resetPartner = effect(() => {
    const id = this.together()?.partner?.id;
    if (id !== this.partnerId) { this.partnerId = id; this.viewingPartner.set(false); }
  });
  private partnerId: string | undefined;
  viewPartner(show: boolean): void {
    this.viewingPartner.set(show && !!this.together()?.partner);
    setTimeout(() => document.getElementById(show ? 'partner-heading' : 'partner-link')?.focus());
  }
  canInvite(): boolean {
    const t = this.together();
    return !!t && !t.partner && !t.invitationExpiresUtc && !this.invitation();
  }
  photoUrl(id: string): string {
    return '/api/auth/together/photo/' + encodeURIComponent(id) + '?v=' + this.photoVersion();
  }
}
