import { Component, input, output } from '@angular/core';
import { DatePipe } from '@angular/common';
export interface QuizRoundSummary {title?:string;id:number;status:string;revision:string;currentIndex:number;mine:boolean;createdUtc:string;}
@Component({selector:'app-quiz-history',imports:[DatePipe],templateUrl:'./quiz-history.html',styleUrl:'./quiz-history.scss'})
export class QuizHistoryComponent {readonly rounds=input<QuizRoundSummary[]>([]);readonly open=output<number>();}
