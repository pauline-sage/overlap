import { NavigationStateService, oneOf, navigationId, navigationBoolean } from '../../navigation-state.service';
import { Component, inject, effect, input, output, signal } from '@angular/core';
import { AlbumsComponent } from '../albums/albums';
import { CozyTimeComponent } from './cozy-time/cozy-time';
import { GetToKnowComponent } from './get-to-know/get-to-know';
import { MakeTogetherComponent } from './make-together/make-together';
import { StayInActivity, StayInRequest } from './stay-in.types';
@Component({selector:'app-stay-in-together',imports:[AlbumsComponent,CozyTimeComponent,GetToKnowComponent,MakeTogetherComponent],templateUrl:'./stay-in-together.html',styleUrl:'./stay-in-together.scss'})
export class StayInTogetherComponent {
  private readonly navigation=inject(NavigationStateService);
  readonly active=input(false);readonly request=input<StayInRequest>({activity:'menu'});readonly ownName=input('Me');readonly partnerName=input('Your person');readonly back=output<void>();
  readonly entry=signal<StayInRequest>({activity:'menu'});
  readonly activity=this.navigation.remember<StayInActivity>('stay.activity',oneOf(['menu','watch','read','quiz','cooking','drawing']),'menu');
  readonly group=this.navigation.remember<'cozy'|'know'|'make'|'albums'>('stay.group',oneOf(['cozy','know','make','albums']),'cozy');
  constructor(){effect(()=>{this.entry.set(this.request());this.activity.set(this.request().activity);const activity=this.request().activity;if(activity==='quiz')this.group.set('know');else if((activity==='cooking'||activity==='drawing'))this.group.set('make');else if(activity==='watch'||activity==='read')this.group.set('cozy');});}
  choose(activity:StayInActivity):void{if(activity==='watch'||activity==='read')this.group.set('cozy');else if(activity==='quiz')this.group.set('know');else if((activity==='cooking'||activity==='drawing'))this.group.set('make');this.entry.set({activity});this.activity.set(activity);queueMicrotask(()=>document.getElementById('dashboard-heading')?.focus());}
}
