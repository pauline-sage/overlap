import { Component, input, output, signal } from '@angular/core';
import { CoffeeDateComponent } from '../coffee-date/coffee-date';
import { SavedPlacesComponent } from './saved-places/saved-places';
import { OutingUpdate } from '../nav-bar/sharing/sharing.service';
@Component({selector:'app-step-out-together',imports:[CoffeeDateComponent,SavedPlacesComponent],templateUrl:'./step-out-together.html',styleUrl:'./step-out-together.scss'})
export class StepOutTogetherComponent {
  readonly proposal=input<OutingUpdate|null>(null);readonly partnerName=input('Your person');readonly back=output<void>();readonly shared=output<void>();
  readonly view=signal<'menu'|'search'|'places'>('menu');
}
