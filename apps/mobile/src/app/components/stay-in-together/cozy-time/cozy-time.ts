import { Component, effect, input, output, viewChild, untracked } from '@angular/core';
import { SharedTimeComponent } from '../../shared-time/shared-time';
import { ReadTogetherComponent } from '../../read-together/read-together';
import { StayInActivity } from '../stay-in.types';
@Component({selector:'app-cozy-time',imports:[SharedTimeComponent,ReadTogetherComponent],templateUrl:'./cozy-time.html',styleUrl:'../activity-section.scss'})
export class CozyTimeComponent {
  readonly active=input(false);readonly activity=input<StayInActivity>('menu');readonly ownName=input('Me');readonly partnerName=input('Your person');readonly partnerPicks=input(false);
  readonly choose=output<StayInActivity>();readonly back=output<void>();private readonly watch=viewChild(SharedTimeComponent);
  constructor(){effect(()=>{if(this.active() && this.activity()==='watch' && this.partnerPicks())untracked(()=>this.watch()?.view('partner'));});}
}
