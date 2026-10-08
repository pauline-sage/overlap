import { NavigationStateService, oneOf, navigationId, navigationBoolean } from '../../navigation-state.service';
import { Component, inject, input, output, signal } from '@angular/core';
import { CoffeeDateComponent } from '../coffee-date/coffee-date';
import { SavedPlacesComponent } from './saved-places/saved-places';
import { OutingUpdate } from '../nav-bar/sharing/sharing.service';
@Component({selector:'app-step-out-together',imports:[CoffeeDateComponent,SavedPlacesComponent],templateUrl:'./step-out-together.html',styleUrl:'./step-out-together.scss'})
export class StepOutTogetherComponent {
  private readonly navigation=inject(NavigationStateService);
  readonly proposal=input<OutingUpdate|null>(null);readonly partnerName=input('Your person');readonly back=output<void>();readonly shared=output<void>();
  readonly view=this.navigation.remember<'menu'|'search'|'places'>('outings.view',oneOf(['menu','search','places']),'menu');
}
