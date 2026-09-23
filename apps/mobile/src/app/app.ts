import { Component } from '@angular/core';
import { LoginComponent } from './components/login/login';

@Component({
  selector: 'app-root',
  imports: [LoginComponent],
  templateUrl: './app.html',
})
export class App {}
