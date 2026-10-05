import { LoadingIndicatorComponent } from '../loading-indicator/loading-indicator';
import { OpeningHoursComponent } from './opening-hours/opening-hours';
import { OutingChoiceComponent } from './outing-choice/outing-choice';
import { OutingCalendarComponent } from './outing-calendar/outing-calendar';
import { Component, inject, input, output, signal, OnDestroy, OnInit } from '@angular/core';
import { DecimalPipe, DatePipe } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../auth.service';
import { SharingService, OutingUpdate } from '../nav-bar/sharing/sharing.service';
interface Area { name: string; timeZone: string; token: string; }
interface Place { id: string; name: string; address: string; distanceKm: number; hours: string | null; hoursStatus: string; mapUrl: string; website: string | null; }
interface SearchResult {
  localStart: string; localEnd: string;
  places: {place:Place;shareToken:string}[]; examined: number; checkedUtc: string;
}
@Component({selector:'app-coffee-date', imports: [LoadingIndicatorComponent,DatePipe, OpeningHoursComponent, FormsModule, DecimalPipe, OutingChoiceComponent, OutingCalendarComponent], templateUrl:'./coffee-date.html', styleUrl:'./coffee-date.scss'})
export class CoffeeDateComponent implements OnDestroy, OnInit {
  readonly proposal=input<OutingUpdate|null>(null);
  ngOnInit():void {const proposal=this.proposal();if(proposal){this.activity=proposal.venue?.activity || 'Coffee';this.step.set(2);}}
  async confirmPlace(token:string):Promise<void> {const proposal=this.proposal();if(proposal && await this.sharing.reply(proposal.revision,'picked',token))this.shared.emit();}
  readonly partnerName = input('Your person');
  readonly back = output<void>();
  readonly shared = output<void>();
  private readonly auth = inject(AuthService);
  readonly sharing = inject(SharingService);
  readonly activities = ['Coffee','Beer','Drinks','Breakfast','Lunch','Dinner'];
  readonly hours = Array.from({length:24}, (_, i) => String(i).padStart(2, '0'));
  readonly minuteChoices = ['00','15','30','45'];
  activity = 'Coffee'; when = 'now'; date = ''; hour = '12'; minute = '00'; minutes = 60; includeUnknownHours = true;
  text = ''; radius = 3; choices: Area[] = []; area: Area | null = null;
  readonly busy = signal(false); readonly locating = signal(false); readonly error = signal('');
  readonly result = signal<SearchResult | null>(null);
  readonly visiblePlaces = signal(false);
  readonly sending = signal(false); readonly notice = signal('');
  readonly step = signal(1);
  readonly picked = signal<SearchResult['places'][number] | null>(null);
  readonly moodOptions = this.activities.map(value=>({value,label:value}));
  readonly hourOptions = this.hours.map(value=>({value,label:value}));
  readonly minuteOptions = this.minuteChoices.map(value=>({value,label:value}));
  readonly whenOptions = [{value:'now',label:'Now'},{value:'hour',label:'In one hour'},{value:'later',label:'Plan a date'}];
  readonly radiusOptions = [1,3,5,10].map(value=>({value:String(value),label:'Within '+value+' km'}));
  choosePlace(match: SearchResult['places'][number]):void {
    this.picked.set(match);this.notice.set('');
    setTimeout(()=>document.getElementById('place-sharing')?.focus());
  }
  next():void {
    if(this.when==='later' && !this.date){this.error.set('Choose a date to continue.');return;}
    this.error.set('');this.step.set(2);this.focusHeading();
  }
  goBack():void { if(this.proposal() && this.step()===2){this.back.emit();return;} if(this.step()>1){this.step.update(x=>x-1);this.focusHeading();}else this.back.emit(); }
  private focusHeading():void { setTimeout(()=>document.getElementById('dashboard-heading')?.focus()); }
  private disposed = false;
  ngOnDestroy(): void { this.disposed = true; this.choices = []; this.text = ''; this.area = null; this.result.set(null); }
  changed(): void { this.result.set(null); this.visiblePlaces.set(false); this.picked.set(null); this.error.set(''); }
  edit(): void { this.area = null; this.choices = []; this.changed(); }
  async findArea(): Promise<void> {
    if (this.locating() || this.busy()) return;
    if (this.text.trim().length < 2) { this.error.set('Enter a city, neighbourhood or address.'); return; }
    this.locating.set(true); this.changed();
    try {
      this.choices = await this.auth.submit<Area[]>('outings/areas', {text:this.text.trim()});
      if (!this.choices.length) this.error.set('No locations found. Include the city and country.');
    } catch (error) { this.failed(error); }
    finally { this.locating.set(false); }
  }
  choose(area: Area): void { this.area = area; this.text = area.name; this.choices = []; this.changed(); }
  async useLocation(): Promise<void> {
    if (this.locating() || this.busy()) return;
    this.changed();
    if (!navigator.geolocation || !window.isSecureContext) { this.error.set('Location needs HTTPS and browser permission. You can enter an address instead.'); return; }
    this.locating.set(true);
    try {
      const position = await new Promise<GeolocationPosition>((resolve, reject) =>
        navigator.geolocation.getCurrentPosition(resolve, reject, {enableHighAccuracy:false, timeout:15000, maximumAge:0}));
      if (this.disposed) return;
      this.choose(await this.auth.submit<Area>('outings/locate', {latitude:position.coords.latitude, longitude:position.coords.longitude}));
    } catch (error) {
      if (error instanceof HttpErrorResponse) this.failed(error);
      else this.error.set('Location is unavailable or permission was denied. Try again or enter an address.');
    } finally { this.locating.set(false); }
  }
  async search(): Promise<void> {
    if (this.busy() || this.locating()) return;
    if (!this.area) { this.error.set('Choose your location from the suggestions or use your current location.'); return; }
    if (this.when === 'later' && !this.date) { this.error.set('Choose a date.'); return; }
    this.changed(); this.busy.set(true);
    try {
      this.result.set(await this.auth.submit<SearchResult>('outings/search', {
        areaToken:this.area.token, radius:Number(this.radius), activity:this.activity,
        localStart:this.when !== 'later' ? null : this.date + 'T' + this.hour + ':' + this.minute,
        startInMinutes:this.when === 'hour' ? 60 : null,
        minutes:Number(this.minutes), includeUnknownHours:this.includeUnknownHours,
        ...(this.proposal() ? {proposalRevision:this.proposal()!.revision} : {}),
      }));
      this.step.set(3); this.focusHeading();
    } catch (error) { this.failed(error); }
    finally { this.busy.set(false); }
  }
  async share(kind: 'venue' | 'clear', venueToken?: string): Promise<void> {
    if (this.sending()) return;
    this.sending.set(true); this.error.set(''); this.notice.set('');
    try {
      await this.auth.submit('outings/update', {kind,venueToken});
      this.notice.set(kind === 'clear' ? 'Update removed.' : 'Place shared with your partner.');
      await this.sharing.load(true);
      if(kind === 'venue') this.shared.emit();
    } catch (error) { this.failed(error); }
    finally { this.sending.set(false); }
  }
  private failed(error: unknown): void {
    this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Unable to complete this action. Try again.' : 'Unable to complete this action. Try again.');
  }
}
