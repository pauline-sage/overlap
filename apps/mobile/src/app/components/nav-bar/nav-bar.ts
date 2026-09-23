import { Component, input, output } from '@angular/core';

export type DashboardPage = 'home' | 'profile' | 'settings';

@Component({
  selector: 'app-nav-bar',
  templateUrl: './nav-bar.html',
  styleUrl: './nav-bar.scss',
})
export class NavBarComponent {
  readonly current = input<DashboardPage>('home');
  readonly disabled = input(false);
  readonly navigate = output<DashboardPage>();
}
