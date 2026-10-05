import { Component, effect, inject, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../../auth.service';
import { LoadingIndicatorComponent } from '../../loading-indicator/loading-indicator';
export interface CustomQuiz {id:number;title:string;revision:string;questions:{prompt:string;choices:string[]}[];}
@Component({selector:'app-quiz-editor',imports:[FormsModule,LoadingIndicatorComponent],templateUrl:'./quiz-editor.html',styleUrl:'./quiz-editor.scss'})
export class QuizEditorComponent {
  readonly quiz=input<CustomQuiz|null>(null);readonly back=output<void>();readonly saved=output<void>();
  private readonly auth=inject(AuthService);readonly busy=signal(false);readonly error=signal('');readonly index=signal(0);
  title='';questions=Array.from({length:5},()=>({prompt:'',choices:['','','','']}));
  constructor(){effect(()=>{const quiz=this.quiz();this.title=quiz?.title ?? '';this.questions=quiz ? quiz.questions.map(q=>({prompt:q.prompt,choices:[...q.choices]})) : Array.from({length:5},()=>({prompt:'',choices:['','','','']}));this.index.set(0);});}
  validQuestion(index:number):boolean{const q=this.questions[index];return !!q?.prompt.trim() && q.choices.every(c=>!!c.trim()) && new Set(q.choices.map(c=>c.trim().toLowerCase())).size===4;}
  ready():boolean{return !!this.title.trim() && this.questions.every((_,index)=>this.validQuestion(index));}
  move(direction:number):void{if(document.activeElement instanceof HTMLElement)document.activeElement.blur();this.index.update(index=>Math.max(0,Math.min(4,index+direction)));}
  async save():Promise<void>{
    if(this.busy() || !this.ready())return;if(document.activeElement instanceof HTMLElement)document.activeElement.blur();this.busy.set(true);this.error.set('');
    try{const quiz=this.quiz();await this.auth.submit('quiz/templates'+(quiz ? '/'+quiz.id : ''),{title:this.title.trim(),questions:this.questions.map(q=>({prompt:q.prompt.trim(),choices:q.choices.map(c=>c.trim())})),revision:quiz?.revision ?? null});this.saved.emit();}
    catch(error){this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Your quiz could not be saved.' : 'Your quiz could not be saved.');}finally{this.busy.set(false);}
  }
}
