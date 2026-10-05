import { Component, ElementRef, input, output, signal, viewChild, computed } from '@angular/core';
export interface QuizCategory {id:string;title:string;personal:boolean;count:number;}
@Component({selector:'app-quiz-guess-setup',templateUrl:'./quiz-guess-setup.html',styleUrl:'./quiz-guess-setup.scss'})
export class QuizGuessSetupComponent {
  readonly categories=input<QuizCategory[]>([]);readonly categoryIds=input<string[]>([]);readonly busy=input(false);readonly connected=input(false);readonly partnerName=input('Your person');
  readonly toggle=output<string>();readonly invite=output<void>();readonly totalQuestions=computed(()=>this.categories().reduce((sum,c)=>sum+c.count,0));
  readonly categoryRail=viewChild<ElementRef<HTMLElement>>('categoryRail');
  readonly categoryIndex=signal(0);
  moveCategory(direction:number):void{this.goToCategory(this.categoryIndex()+direction);}
  goToCategory(index:number):void{
    const rail=this.categoryRail()?.nativeElement;if(!rail)return;
    const next=Math.max(0,Math.min(this.categories().length-1,index));
    const card=rail.children[next] as HTMLElement|undefined;if(!card)return;
    this.categoryIndex.set(next);
    rail.scrollTo({left:card.offsetLeft,behavior:window.matchMedia('(prefers-reduced-motion: reduce)').matches ? 'instant' : 'smooth'});
  }
  categoryScrolled(event:Event):void{
    const rail=event.target as HTMLElement;let nearest=0,distance=Infinity;
    Array.from(rail.children).forEach((card,index)=>{const delta=Math.abs((card as HTMLElement).offsetLeft-rail.scrollLeft);if(delta<distance){nearest=index;distance=delta;}});
    this.categoryIndex.set(nearest);
  }
  categoryKey(event:KeyboardEvent):void{
    if(event.key==='ArrowRight')this.moveCategory(1);else if(event.key==='ArrowLeft')this.moveCategory(-1);else if(event.key==='Home')this.goToCategory(0);else if(event.key==='End')this.goToCategory(this.categories().length-1);else return;
    event.preventDefault();
  }
}
