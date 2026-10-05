import { Component, effect, input, output, signal } from '@angular/core';
import { CozyTimeComponent } from './cozy-time/cozy-time';
import { GetToKnowComponent } from './get-to-know/get-to-know';
import { MakeTogetherComponent } from './make-together/make-together';
import { StayInActivity, StayInRequest } from './stay-in.types';
@Component({selector:'app-stay-in-together',imports:[CozyTimeComponent,GetToKnowComponent,MakeTogetherComponent],templateUrl:'./stay-in-together.html',styleUrl:'./stay-in-together.scss'})
export class StayInTogetherComponent {
  readonly active=input(false);readonly request=input<StayInRequest>({activity:'menu'});readonly ownName=input('Me');readonly partnerName=input('Your person');readonly back=output<void>();
  readonly entry=signal<StayInRequest>({activity:'menu'});
  readonly activity=signal<StayInActivity>('menu');
  readonly group=signal<'cozy'|'know'|'make'>('cozy');
  constructor(){effect(()=>{this.entry.set(this.request());this.activity.set(this.request().activity);const activity=this.request().activity;if(activity==='quiz')this.group.set('know');else if(activity==='cooking')this.group.set('make');else if(activity==='watch'||activity==='read')this.group.set('cozy');});}
  choose(activity:StayInActivity):void{if(activity==='watch'||activity==='read')this.group.set('cozy');else if(activity==='quiz')this.group.set('know');else if(activity==='cooking')this.group.set('make');this.entry.set({activity});this.activity.set(activity);queueMicrotask(()=>document.getElementById('dashboard-heading')?.focus());}
}
