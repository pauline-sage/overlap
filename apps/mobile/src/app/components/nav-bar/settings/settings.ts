import { ConnectionDataComponent } from './connection-data';
import { CalendarNotificationsComponent } from './calendar-notifications/calendar-notifications';
import { SharingComponent } from '../sharing/sharing';
import { PhoneRemindersComponent } from './phone-reminders/phone-reminders';
import { PrivacyNoticeComponent } from '../../privacy-notice/privacy-notice';

import { FormsModule } from '@angular/forms';
import { AuthService } from '../../../auth.service';
import { HttpErrorResponse } from '@angular/common/http';
import { IdleSessionService } from '../../../idle-session.service';
import { LoadingIndicatorComponent } from '../../loading-indicator/loading-indicator';
import { Component, inject, signal, input, output } from '@angular/core';
@Component({
  selector: 'app-settings',
  imports: [ConnectionDataComponent,CalendarNotificationsComponent,FormsModule, LoadingIndicatorComponent,PrivacyNoticeComponent, SharingComponent,PhoneRemindersComponent],
  templateUrl: './settings.html',
  styleUrl: './settings.scss',
})
export class SettingsComponent {
  private readonly auth=inject(AuthService);
  private readonly idle=inject(IdleSessionService);
  readonly archiveOpen=signal(false);
  readonly confirming=signal(false);
  readonly deleting=signal(false);
  readonly error=signal('');
  password='';
  async deleteAccount():Promise<void> {
    if(this.deleting() || !this.password || !this.confirming())return;
    this.deleting.set(true);this.error.set('');
    try {
      await this.auth.submit('delete-account',{password:this.password,confirm:true});
      this.password='';this.idle.broadcastLogout();this.idle.stop();
      window.location.replace(window.location.pathname);
    } catch(error) {
      this.password='';
      this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Unable to delete your account. Try again.' : 'Unable to delete your account. Try again.');
    } finally {this.deleting.set(false);}
  }
  cancelDeletion():void {this.confirming.set(false);this.password='';this.error.set('');}

  readonly busy = input(false);
  readonly changeEmail = output<void>();
  readonly changePassword = output<void>();
  readonly signOut = output<void>();
}
