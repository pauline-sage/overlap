import { Component, input, output, signal } from '@angular/core';
import { CustomQuiz } from '../editor/quiz-editor';
@Component({selector:'app-quiz-library',templateUrl:'./quiz-library.html',styleUrl:'./quiz-library.scss'})
export class QuizLibraryComponent {
  readonly templates=input<CustomQuiz[]>([]);readonly busy=input(false);readonly connected=input(false);readonly partnerName=input('Your person');
  readonly removingTemplate=signal<number|null>(null);readonly create=output<void>();readonly edit=output<CustomQuiz>();readonly invite=output<CustomQuiz>();readonly remove=output<CustomQuiz>();
}
