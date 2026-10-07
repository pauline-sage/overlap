import { LoadingIndicatorComponent } from '../../loading-indicator/loading-indicator';
﻿import { DateCardsComponent } from '../../date-cards/date-cards';
import { Component, inject, input, output } from '@angular/core';
import { OutingUpdate, SharingService, Category } from './sharing.service';
@Component({selector:'app-sharing',imports: [LoadingIndicatorComponent,DateCardsComponent],templateUrl:'./sharing.html',styleUrl:'./sharing.scss'})
export class SharingComponent {
  readonly notifications=input(false); readonly pickYours=output<OutingUpdate>(); readonly openCooking=output<number>(); readonly openContest=output<number>(); readonly openPicks=output<void>(); readonly sharing=inject(SharingService);
  readonly categories: {key:Category;label:string;description:string}[] = [
    {key:'profile',label:'Profile & online presence',description:'Your display name, photo and whether you are online. No location or activity history.'},
    {key:'picks',label:'Films - series I picked',description:'Only titles you choose to share, never your whole private list.'},
    {key:'cooking',label:'Recipes I have made',description:'Share your Made it history and recipe ratings with your person. Off keeps them private; saved recipes and explicit recipe shares stay shared.'},
    {key:'books',label:'Books I want to read',description:'Show your bookshelf to your person. Off keeps your reading interests private; books read together stay shared.'},
  ];
  readonly sections = [
    {title:'Personal',categories:this.categories.filter(x=>x.key==='profile' || x.key==='presence')},
    {title:'Watch & Read',categories:this.categories.filter(x=>x.key==='picks' || x.key==='books')},
    {title:'Cooking',categories:this.categories.filter(x=>x.key==='cooking')},
  ];
  toggle(category: Category, event: Event): void {
    const control = event.target as HTMLInputElement;
    const enable = control.checked;
    control.checked = this.outgoing(category) === 'accepted';
    void this.sharing.act(enable ? 'request' : 'cancel', category === 'profile' ? ['profile', 'presence'] : category);
  }
  label(category: Category): string { return this.categories.find(x=>x.key===category)?.label || category; }
  description(category: Category): string { return this.categories.find(x=>x.key===category)?.description || ''; }
  outgoing(category: Category): string {
    const status = (key: Category) => this.sharing.state()?.outgoing.find(x=>x.category===key)?.status || 'private';
    if(category === 'profile') {
      const profile = status('profile') === 'accepted', presence = status('presence') === 'accepted';
      return profile && presence ? 'accepted' : profile || presence ? 'partial' : 'private';
    }
    return status(category);
  }
}
