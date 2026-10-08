import { DateCardsComponent } from '../date-cards/date-cards';
import { SharingComponent } from './sharing/sharing';
import { OutingUpdate, SharingService } from './sharing/sharing.service';
import { SettingsComponent } from './settings/settings';
import { Component, inject, input, output } from '@angular/core';

export type DashboardPage = 'home' | 'dates' | 'profile' | 'settings' | 'notifications';

@Component({
  selector: 'app-nav-bar',
  imports: [SettingsComponent, SharingComponent, DateCardsComponent],

  templateUrl: './nav-bar.html',
  styleUrl: './nav-bar.scss',
})
export class NavBarComponent {
  readonly sharing = inject(SharingService);
  readonly openQuiz=output<number>();readonly openDrawing=output<number>();
  readonly pickYours=output<OutingUpdate>(); readonly openCooking=output<number>(); readonly openContest=output<number>(); readonly openPicks = output<void>();
  readonly current = input<DashboardPage>('home');
  readonly disabled = input(false);
  readonly changeEmail = output<void>();
  readonly changePassword = output<void>();
  readonly signOut = output<void>();
  readonly navigate = output<DashboardPage>();
}
