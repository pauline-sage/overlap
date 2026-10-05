import { Component, input, output } from '@angular/core';
export type QuizArea='home'|'guess'|'custom'|'history';
@Component({selector:'app-quiz-home',templateUrl:'./quiz-home.html',styleUrl:'./quiz-home.scss'})
export class QuizHomeComponent {readonly pending=input(0);readonly choose=output<QuizArea>();}
