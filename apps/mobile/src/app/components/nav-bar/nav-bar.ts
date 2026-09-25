import { SettingsComponent } from './settings/settings';
import { Component, input, output } from '@angular/core';

export type DashboardPage = 'home' | 'profile' | 'settings';

@Component({
  selector: 'app-nav-bar',
  imports: [SettingsComponent],
  templateUrl: './nav-bar.html',
  styleUrl: './nav-bar.scss',
})
export class NavBarComponent {
  readonly current = input<DashboardPage>('home');
  readonly disabled = input(false);
  readonly changeEmail = output<void>();
  readonly changePassword = output<void>();
  readonly signOut = output<void>();
  readonly navigate = output<DashboardPage>();
}
