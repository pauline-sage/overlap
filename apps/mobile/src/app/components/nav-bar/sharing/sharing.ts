import { LoadingIndicatorComponent } from '../../loading-indicator';
﻿import { DateCardsComponent } from '../../date-cards/date-cards';
import { Component, inject, input, output } from '@angular/core';
import { OutingUpdate, SharingService, Category } from './sharing.service';
@Component({selector:'app-sharing',imports: [LoadingIndicatorComponent,DateCardsComponent],templateUrl:'./sharing.html',styleUrl:'./sharing.scss'})
export class SharingComponent {
  readonly notifications=input(false); readonly pickYours=output<OutingUpdate>(); readonly openPicks=output<void>(); readonly sharing=inject(SharingService);
  readonly categories: {key:Category;label:string;description:string}[] = [
    {key:'profile',label:'Profile',description:'Your display name and photo, including future updates.'},
    {key:'presence',label:'Online presence',description:'Whether you are currently online. No location or activity history.'},
    {key:'picks',label:'Films - series I picked',description:'Only titles you choose to share, never your whole private list.'},
    {key:'books',label:'Books I want to read',description:'Show your bookshelf to your person. Off keeps your reading interests private; books read together stay shared.'},
  ];
  toggle(category: Category, event: Event): void {
    const control = event.target as HTMLInputElement;
    const enable = control.checked;
    control.checked = this.outgoing(category) === 'accepted';
    void this.sharing.act(enable ? 'request' : 'cancel', category);
  }
  label(category: Category): string { return this.categories.find(x=>x.key===category)?.label || category; }
  description(category: Category): string { return this.categories.find(x=>x.key===category)?.description || ''; }
  outgoing(category: Category): string { return this.sharing.state()?.outgoing.find(x=>x.category===category)?.status || 'private'; }
}
