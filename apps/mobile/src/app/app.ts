import { Component } from '@angular/core';
import { LoginComponent } from './components/login/login';
import { KeyboardDismissalDirective } from './keyboard-dismissal.directive';

@Component({
  selector: 'app-root',
  imports: [LoginComponent],
  hostDirectives: [KeyboardDismissalDirective],
  templateUrl: './app.html',
})
export class App {}
