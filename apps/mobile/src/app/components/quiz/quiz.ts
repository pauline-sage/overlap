import { QuizHomeComponent, QuizArea } from './home/quiz-home';
import { QuizGuessSetupComponent, QuizCategory } from './guess-setup/quiz-guess-setup';
import { QuizLibraryComponent } from './library/quiz-library';
import { QuizHistoryComponent, QuizRoundSummary } from './history/quiz-history';
import { QuizEditorComponent, CustomQuiz } from './editor/quiz-editor';
import { SharingService } from '../nav-bar/sharing/sharing.service';
import { Component, DestroyRef, inject, input, output, signal, computed, OnInit } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../auth.service';
import { LoadingIndicatorComponent } from '../loading-indicator/loading-indicator';
interface Reveal {ownAnswer:number|null;ownGuess:number|null;partnerAnswer:number|null;partnerGuess:number|null;ownSkipped:boolean;partnerSkipped:boolean;ownCorrect:boolean|null;partnerCorrect:boolean|null;}
interface Question {id:string;categoryId:string;prompt:string;choices:string[];ownSubmitted:boolean;partnerSubmitted:boolean;ownAnswer:{ownChoice:number|null;guessChoice:number|null;skipped:boolean}|null;reveal:Reveal|null;}
interface Round extends QuizRoundSummary {questions:Question[];}
@Component({selector:'app-quiz',imports:[QuizHomeComponent,QuizGuessSetupComponent,QuizLibraryComponent,QuizHistoryComponent,QuizEditorComponent,LoadingIndicatorComponent],templateUrl:'./quiz.html',styleUrl:'./quiz.scss'})
export class QuizComponent implements OnInit {
  readonly area=signal<QuizArea>('home');
  readonly pendingRounds=computed(()=>this.rounds().filter(round=>round.status==='pending' && !round.mine).length);
  goBack():void{if(this.selectedId())this.close();else if(this.area()!=='home'){this.area.set('home');this.notice.set('');}else this.back.emit();}

  readonly templates=signal<CustomQuiz[]>([]);readonly editorOpened=signal(false);readonly editingTemplate=signal<CustomQuiz|null>(null);
  newTemplate():void{this.editingTemplate.set(null);this.editorOpened.set(true);}
  editTemplate(quiz:CustomQuiz):void{this.editingTemplate.set(quiz);this.editorOpened.set(true);}
  async templateSaved():Promise<void>{this.editorOpened.set(false);this.notice.set('Your quiz is saved. Invite your person when you are ready.');await this.load(true);}
  async inviteCustom(quiz:CustomQuiz):Promise<void>{
    if(this.busy())return;this.busy.set(true);this.error.set('');
    try{const result=await this.auth.submit<{id:number}>('quiz/rounds',{templateId:quiz.id});await this.open(result.id);}catch(error){this.problem(error);}finally{this.busy.set(false);}
  }
  async removeTemplate(quiz:CustomQuiz):Promise<void>{
    if(this.busy())return;this.busy.set(true);this.error.set('');
    try{await this.auth.submit('quiz/templates/'+quiz.id+'/remove',{revision:quiz.revision});await this.load(true);}catch(error){this.problem(error);}finally{this.busy.set(false);}
  }
  readonly initialRoundId=input(0);
  private readonly sharing=inject(SharingService,{optional:true});
  readonly back=output<void>();readonly partnerName=input('Your person');
  private readonly http=inject(HttpClient);private readonly auth=inject(AuthService);
  readonly categories=signal<QuizCategory[]>([]);readonly categoryIds=signal<string[]>([]);
  readonly rounds=signal<QuizRoundSummary[]>([]);readonly connected=signal(false);
  readonly selectedId=signal(0);readonly round=signal<Round|null>(null);
  readonly question=computed(()=>this.round()?.questions[this.round()!.currentIndex] ?? null);
  readonly reviewOpened=signal(false);readonly reviewIndex=signal(0);
  readonly reviewQuestion=computed(()=>this.round()?.questions[this.reviewIndex()] ?? null);
  openReview():void{this.reviewIndex.set(0);this.reviewOpened.set(true);}
  moveReview(direction:number):void{this.reviewIndex.update(index=>Math.max(0,Math.min((this.round()?.questions.length ?? 1)-1,index+direction)));}
  playAnother():void{this.close();this.area.set('home');this.notice.set('Your previous answers stay saved. Choose how to play next.');}
  partnerPrompt(prompt:string):string{return prompt.replace(/\bmyself\b/gi,'themselves').replace(/\bmy\b/gi,'their').replace(/\bme\b/gi,'them').replace(/\bI\b/g,'they').replace(/\bam they\b/gi,'are they');}
  readonly ownChoice=signal<number|null>(null);readonly guessChoice=signal<number|null>(null);
  readonly busy=signal(false);readonly error=signal('');readonly notice=signal('');readonly confirmCancel=signal(false);readonly confirmRemove=signal(false);
  readonly totalQuestions=computed(()=>this.categories().reduce((sum,x)=>sum+x.count,0));
  readonly scored=computed(()=>this.round()?.questions.filter(x=>x.reveal?.ownCorrect!==null && x.reveal?.ownCorrect!==undefined).length ?? 0);
  readonly ownScore=computed(()=>this.round()?.questions.filter(x=>x.reveal?.ownCorrect===true).length ?? 0);
  readonly partnerScore=computed(()=>this.round()?.questions.filter(x=>x.reveal?.partnerCorrect===true).length ?? 0);
  private loading=false;private version=0;private destroyed=false;private draftKey='';
  constructor(){
    let tick=0;const refresh=()=>{if(document.visibilityState==='visible')void this.load();};
    const timer=setInterval(()=>{if(++tick%(this.selectedId()?1:5)===0)refresh();},3000);
    document.addEventListener('visibilitychange',refresh);
    inject(DestroyRef).onDestroy(()=>{this.destroyed=true;++this.version;clearInterval(timer);document.removeEventListener('visibilitychange',refresh);});
  }
  async ngOnInit():Promise<void>{
    this.busy.set(true);
    try{const categories=await firstValueFrom(this.http.get<QuizCategory[]>('/api/auth/quiz/catalog'));if(!this.destroyed){this.categories.set(categories);this.categoryIds.set(categories.filter(x=>!x.personal).map(x=>x.id));}}
    catch{this.error.set('Quiz categories could not be loaded. Try again.');}
    finally{this.busy.set(false);if(this.initialRoundId()){this.area.set('history');await this.open(this.initialRoundId());}else await this.load();}
  }
  categoryTitle(id:string):string{return id==='custom' ? 'Your own questions' : this.categories().find(x=>x.id===id)?.title ?? id;}
  roundCategories():string[]{return [...new Set(this.round()?.questions.map(x=>x.categoryId) ?? [])];}
  toggleCategory(id:string):void{this.categoryIds.update(ids=>ids.includes(id)?ids.filter(x=>x!==id):[...ids,id]);}
  answerLabel(q:Question,value:number|null):string{return value===null ? 'Skipped' : q.choices[value];}
  resultLabel(correct:boolean|null):string{return correct===null ? 'Not scored: a question was skipped' : correct ? 'Correct' : 'Incorrect';}
  async open(id:number):Promise<void>{++this.version;this.selectedId.set(id);this.round.set(null);this.error.set('');this.notice.set('');this.draftKey='';this.reviewOpened.set(false);this.reviewIndex.set(0);this.confirmCancel.set(false);this.confirmRemove.set(false);this.busy.set(true);try{await this.load(true);}finally{this.busy.set(false);}}
  close():void{this.reviewOpened.set(false);this.notice.set('');++this.version;this.selectedId.set(0);this.round.set(null);this.confirmCancel.set(false);this.confirmRemove.set(false);this.error.set('');void this.load(true);}
  async load(force=false):Promise<void>{
    if(!force && (this.loading || this.busy()))return;
    this.loading=true;const version=++this.version;const id=this.selectedId();
    try{
      if(id){
        const round=await firstValueFrom(this.http.get<Round>('/api/auth/quiz/rounds/'+id));
        if(!this.destroyed && version===this.version && id===this.selectedId()){
          this.round.set(round);const key=id+':'+round.currentIndex;
          if(key!==this.draftKey){this.draftKey=key;this.notice.set('');this.ownChoice.set(null);this.guessChoice.set(null);}
        }
      }else{
        const [result,templates]=await Promise.all([firstValueFrom(this.http.get<{connected:boolean;rounds:QuizRoundSummary[]}>('/api/auth/quiz/rounds')),firstValueFrom(this.http.get<CustomQuiz[]>('/api/auth/quiz/templates'))]);
        if(!this.destroyed && version===this.version){this.connected.set(result.connected);this.rounds.set(result.rounds);this.templates.set(templates);}
      }
    }catch{if(!this.destroyed && version===this.version)this.error.set('This quiz could not be refreshed. Your submitted answers remain saved.');}
    finally{if(version===this.version)this.loading=false;}
  }
  async create():Promise<void>{
    if(this.busy() || !this.connected() || !this.categoryIds().length)return;
    this.busy.set(true);this.error.set('');
    try{const result=await this.auth.submit<{id:number}>('quiz/rounds',{categoryIds:this.categoryIds()});await this.open(result.id);}
    catch(error){this.problem(error);}finally{this.busy.set(false);}
  }
  async act(action:'accept'|'decline'|'cancel'|'next'):Promise<void>{
    const round=this.round();if(!round || this.busy())return;
    this.busy.set(true);this.error.set('');++this.version;
    try{await this.auth.submit('quiz/rounds/'+round.id+'/action',{revision:round.revision,action});this.confirmCancel.set(false);await this.sharing?.load(true);}
    catch(error){this.problem(error);}finally{await this.load(true);this.busy.set(false);}
  }
  async submit(skipped=false):Promise<void>{
    const round=this.round(),question=this.question();if(!round || !question || question.ownSubmitted || this.busy())return;
    if(!skipped && (this.ownChoice()===null || this.guessChoice()===null))return;
    this.busy.set(true);this.error.set('');++this.version;
    try{await this.auth.submit('quiz/rounds/'+round.id+'/answer',{questionIndex:round.currentIndex,ownChoice:skipped?null:this.ownChoice(),guessChoice:skipped?null:this.guessChoice(),skipped});this.notice.set('Your answers are saved and locked.');}
    catch(error){this.problem(error);}finally{await this.load(true);this.busy.set(false);}
  }
  async remove():Promise<void>{
    const round=this.round();if(!round || this.busy() || !this.confirmRemove())return;
    this.busy.set(true);this.error.set('');++this.version;
    try{await this.auth.submit('quiz/rounds/'+round.id+'/remove',{revision:round.revision});this.close();}
    catch(error){this.problem(error);}finally{this.busy.set(false);}
  }
  private problem(error:unknown):void{if(!this.destroyed)this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Quiz is unavailable. Please try again.' : 'Quiz is unavailable. Please try again.');}
}
