import { Component, input, output } from '@angular/core';
import { QuizComponent } from '../../quiz/quiz';
import { StayInActivity } from '../stay-in.types';
@Component({selector:'app-get-to-know',imports:[QuizComponent],templateUrl:'./get-to-know.html',styleUrl:'../activity-section.scss'})
export class GetToKnowComponent {
  readonly active=input(false);readonly activity=input<StayInActivity>('menu');readonly partnerName=input('Your person');readonly roundId=input(0);
  readonly choose=output<StayInActivity>();readonly back=output<void>();
}
