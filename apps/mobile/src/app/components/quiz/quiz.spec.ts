import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { AuthService } from '../../auth.service';
import { QuizComponent } from './quiz';
const categories=[{id:'everyday',title:'Everyday life',personal:false,count:10},{id:'affection',title:'Affection',personal:true,count:10}];
const questions=()=>Array.from({length:5},(_,i)=>({id:'everyday-'+i,categoryId:'everyday',prompt:'Question '+i,choices:['One','Two','Three','Four'],ownSubmitted:false,partnerSubmitted:false,ownAnswer:null as {ownChoice:number;guessChoice:number;skipped:boolean}|null,reveal:null as {ownAnswer:number;ownGuess:number;partnerAnswer:number;partnerGuess:number;ownSkipped:boolean;partnerSkipped:boolean;ownCorrect:boolean;partnerCorrect:boolean}|null}));
describe('Quiz together',()=>{
  it('opts out of personal categories and follows live reveals without replacing unsent choices',async()=>{
    vi.stubGlobal('matchMedia',vi.fn(()=>({matches:false})));vi.useFakeTimers();const visibility=vi.spyOn(document,'visibilityState','get').mockReturnValue('visible');
    let state={id:7,revision:'first',status:'active',currentIndex:0,mine:true,createdUtc:'2026-10-05T10:00:00Z',questions:questions()};
    const http={get:vi.fn((url:string)=>of(url.endsWith('/templates') ? [] : url.endsWith('/catalog') ? categories : url.endsWith('/rounds') ? {connected:true,rounds:[]} : JSON.parse(JSON.stringify(state))))};
    const auth={submit:vi.fn(async(path:string)=>{if(path.endsWith('/answer')){state={...state,revision:'answered'};state.questions[0]={...state.questions[0],ownSubmitted:true,ownAnswer:{ownChoice:0,guessChoice:2,skipped:false}};}if(path.endsWith('/action'))state={...state,currentIndex:1,revision:'next'};return {id:7};})};
    TestBed.configureTestingModule({imports:[QuizComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth}]});const f=TestBed.createComponent(QuizComponent);
    try{
      f.detectChanges();await vi.advanceTimersByTimeAsync(0);f.detectChanges();const c=f.componentInstance;
      expect(c.categoryIds()).toEqual(['everyday']);
      expect(f.nativeElement.querySelector('app-quiz-home')).not.toBeNull();expect(f.nativeElement.querySelector('app-quiz-guess-setup')).toBeNull();
      const guessButton=Array.from(f.nativeElement.querySelectorAll('.quiz-choice') as NodeListOf<HTMLButtonElement>)[0];guessButton.click();f.detectChanges();
      expect(f.nativeElement.querySelector('app-quiz-library')).toBeNull();
      expect(f.nativeElement.querySelector('[aria-roledescription=carousel]')).not.toBeNull();
      const rail=f.nativeElement.querySelector('.category-carousel');rail.scrollTo=vi.fn();
      f.nativeElement.querySelector('[aria-label="Next category"]').click();f.detectChanges();expect(f.nativeElement.querySelector('.carousel-controls span').textContent).toContain('2 / 2');
      f.nativeElement.querySelector('[aria-label="Add Affection"]').click();f.detectChanges();expect(c.categoryIds()).toEqual(['everyday','affection']);
      f.nativeElement.querySelector('[aria-label="Remove Affection"]').click();expect(c.categoryIds()).toEqual(['everyday']);expect(f.nativeElement.textContent).toContain('Personal, opt in');await c.open(7);f.detectChanges();
      expect(f.nativeElement.textContent).toContain('1. Answer for yourself');
      expect(f.nativeElement.querySelectorAll('input[type=radio]')).toHaveLength(4);
      f.nativeElement.querySelector('input[type=radio]').click();c.nextAnswerStep();f.detectChanges();
      expect(f.nativeElement.textContent).toContain('2. Guess for Your person');
      f.nativeElement.querySelectorAll('input[type=radio]')[2].click();
      await vi.advanceTimersByTimeAsync(3000);expect(c.ownChoice()).toBe(0);expect(c.guessChoice()).toBe(2);
      await c.submit();f.detectChanges();expect(auth.submit).toHaveBeenCalledWith('quiz/rounds/7/answer',{questionIndex:0,ownChoice:0,guessChoice:2,skipped:false});expect(f.nativeElement.textContent).toContain('Waiting for Your person');expect(f.nativeElement.textContent).not.toContain('Did you know each other?');
      state.questions[0]={...state.questions[0],partnerSubmitted:true,reveal:{ownAnswer:0,ownGuess:2,partnerAnswer:2,partnerGuess:0,ownSkipped:false,partnerSkipped:false,ownCorrect:true,partnerCorrect:true}};
      await vi.advanceTimersByTimeAsync(3000);f.detectChanges();expect(f.nativeElement.textContent).toContain('Your guess about Your person');expect(f.nativeElement.textContent).toContain('You got it right');expect(f.nativeElement.querySelectorAll('details')).toHaveLength(0);
      await c.act('next');f.detectChanges();expect(c.round()?.currentIndex).toBe(1);expect(c.ownChoice()).toBeNull();expect(c.guessChoice()).toBeNull();expect(c.answerStep()).toBe('own');
    }finally{f.destroy();visibility.mockRestore();vi.useRealTimers();vi.unstubAllGlobals();}
  });
  it('requires a partner invitation response and confirms removal of saved shared answers',async()=>{
    let state={id:7,revision:'pending',status:'pending',currentIndex:0,mine:false,createdUtc:'2026-10-05T10:00:00Z',questions:questions()};
    const http={get:vi.fn((url:string)=>of(url.endsWith('/templates') ? [] : url.endsWith('/catalog') ? categories : url.endsWith('/rounds') ? {connected:true,rounds:[state]} : JSON.parse(JSON.stringify(state))))};const auth={submit:vi.fn().mockResolvedValue({})};
    TestBed.configureTestingModule({imports:[QuizComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth}]});const f=TestBed.createComponent(QuizComponent);f.componentRef.setInput('initialRoundId',7);f.detectChanges();await vi.waitFor(()=>expect(f.componentInstance.round()?.status).toBe('pending'));f.detectChanges();
    expect(f.nativeElement.textContent).toContain("Let's play");expect(f.nativeElement.querySelector('input[type=radio]')).toBeNull();await f.componentInstance.act('accept');expect(auth.submit).toHaveBeenLastCalledWith('quiz/rounds/7/action',{revision:'pending',action:'accept'});
    state={...state,status:'completed',revision:'saved',questions:questions().map(q=>({...q,ownSubmitted:true,partnerSubmitted:true,reveal:{ownAnswer:0,ownGuess:2,partnerAnswer:2,partnerGuess:0,ownSkipped:false,partnerSkipped:false,ownCorrect:true,partnerCorrect:true}}))};await f.componentInstance.load();f.detectChanges();expect(f.nativeElement.textContent).toContain('Our quizzes');
    const c=f.componentInstance;expect(f.nativeElement.querySelector('.review-card')).toBeNull();
    c.openReview();f.detectChanges();expect(f.nativeElement.querySelectorAll('.review-card')).toHaveLength(1);expect(f.nativeElement.querySelector('.review-card h3').textContent).toBe('Question 0');
    c.moveReview(1);f.detectChanges();expect(f.nativeElement.querySelector('.review-card h3').textContent).toBe('Question 1');
    c.playAnother();await c.load(true);f.detectChanges();expect(c.selectedId()).toBe(0);expect(c.rounds()[0].id).toBe(7);expect(auth.submit).toHaveBeenCalledTimes(1);await c.open(7);f.detectChanges();
    await f.componentInstance.remove();expect(auth.submit).toHaveBeenCalledTimes(1);f.componentInstance.confirmRemove.set(true);f.detectChanges();expect(f.nativeElement.textContent).toContain('all its answers and guesses for both');await f.componentInstance.remove();expect(auth.submit).toHaveBeenLastCalledWith('quiz/rounds/7/remove',{revision:'saved'});f.destroy();
  });
});
