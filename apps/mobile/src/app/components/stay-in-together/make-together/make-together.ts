import { Component, input, output } from '@angular/core';
import { DrawTogetherComponent } from '../../draw-together/draw-together';
import { CookingComponent } from '../../cooking/cooking';
import { StayInActivity } from '../stay-in.types';
@Component({selector:'app-make-together',imports:[CookingComponent,DrawTogetherComponent],templateUrl:'./make-together.html',styleUrl:'../activity-section.scss'})
export class MakeTogetherComponent {
  readonly active=input(false);readonly activity=input<StayInActivity>('menu');readonly ownName=input('Me');readonly partnerName=input('Your person');readonly recipeId=input(0);readonly contestId=input(0);
  readonly choose=output<StayInActivity>();readonly back=output<void>();
}
