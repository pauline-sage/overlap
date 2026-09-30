import { SharingComponent } from '../sharing/sharing';
import { PrivacyNoticeComponent } from '../../privacy-notice/privacy-notice';

import { Component, input, output } from '@angular/core';
@Component({
  selector: 'app-settings',
  imports: [PrivacyNoticeComponent, SharingComponent],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class SettingsComponent {
  readonly busy = input(false);
  readonly changeEmail = output<void>();
  readonly changePassword = output<void>();
  readonly signOut = output<void>();
}
