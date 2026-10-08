import { Injectable, inject, signal, computed, OnDestroy } from '@angular/core';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../../auth.service';
export type Category = 'profile' | 'presence' | 'picks' | 'books' | 'cooking';
interface Grant { category: Category; status: 'pending' | 'accepted' | 'declined' | 'withdrawn'; }
export interface OutingUpdate { dismissed?:boolean; cancelled?:boolean; cancelledByMe?:boolean; cancellationReason?:string; replyKind?: 'picked'|'surprise'; replyRevision?: string; replyVenue?: OutingUpdate['venue']; mine: boolean; onMyWay: boolean; updatedUtc: string; revision: string; unread: boolean; venue: { name: string; address: string; mapUrl: string; website?: string; activity?: string; startUtc?: string; timeZone?: string } | null; }
export interface MoviePick { dismissed?:boolean; id:number;revision:string;mine:boolean;status:string;unread:boolean;shared:boolean;title:{title:string;format:string;poster?:string;ownLink:string;partnerLink:string}; }
export interface CookingContest {redrawPending?:boolean;redrawMine?:boolean;recipeTitle?:string;id:number;revision:string;status:'pending'|'accepted'|'declined'|'cancelled';mine:boolean;startUtc:string;timeZone:string;availableMinutes:number;unread:boolean;dismissed:boolean;}
interface SharingState { drawingInvitations?:{id:number;prompt:string;seconds:number;drawingCount?:number;drawingNumber?:number}[]; quizInvitations?:{id:number}[]; unreadContests?:number;cookingContests?:CookingContest[]; unreadRecipes?:number;recipeShares?:{id:number;revision:string;title:string;unread:boolean;dismissed:boolean}[]; partnerName?:string;unreadOutings?:number;unreadPicks?:number;moviePicks?:MoviePick[]; connected: boolean; incoming: Grant[]; outgoing: Grant[]; receivedPicks: number; outingUpdates?: OutingUpdate[]; }
@Injectable()
export class SharingService implements OnDestroy {
  private readonly http = inject(HttpClient); private readonly auth = inject(AuthService);
  readonly state = signal<SharingState | null>(null); readonly busy = signal(false); readonly error = signal('');
  readonly count = computed(() => {
    const state = this.state(); return (state?.drawingInvitations?.length ?? 0) + (state?.quizInvitations?.length ?? 0) + (state?.unreadContests ?? 0) + (state?.unreadRecipes ?? 0) + (state?.unreadOutings ?? 0) + (state?.unreadPicks ?? state?.receivedPicks ?? 0);
  });
  private loading = false; private disposed = false; private version = 0;
  private readonly timer = setInterval(()=>{ if(document.visibilityState==='visible') void this.load(); },15000);
  constructor() { void this.load(); }
  async load(force = false): Promise<void> {
    if((this.loading && !force) || this.disposed) return;
    const version = ++this.version; this.loading=true;
    try { const state=await firstValueFrom(this.http.get<SharingState>('/api/auth/privacy')); if(!this.disposed && version === this.version) { this.state.set(state); this.error.set(''); } }
    catch { if(!this.disposed && version === this.version) { this.state.set(null); this.error.set('Sharing settings are unavailable. Try again.'); } }
    finally { if(version === this.version) this.loading=false; }
  }
  async act(action: 'request' | 'cancel', category: Category | Category[]): Promise<void> {
    if(this.busy()) return; this.busy.set(true); this.error.set('');
    try {
      for(const key of Array.isArray(category) ? category : [category]) await this.auth.submit('privacy/'+action,{category:key});
      await this.load(true);
    }
    catch(error) { await this.load(true); this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Unable to update sharing.' : 'Unable to update sharing.'); }
    finally { this.busy.set(false); }
  }
  async markRead(kind:'outing'|'outing-reply'|'pick'|'recipe'|'contest', revision:string, id?:number, dismiss=false):Promise<void> {
    if(this.busy())return;
    this.busy.set(true);
    try { await this.auth.submit('privacy/read',{kind,revision,id,dismiss});await this.load(true); }
    catch { this.error.set('Unable to mark this update as read.'); }
    finally { this.busy.set(false); }
  }
  async contestAction(contest:CookingContest,action:'accept'|'decline'|'cancel'):Promise<boolean> {
    if(this.busy())return false;this.busy.set(true);this.error.set('');
    try {await this.auth.submit('cooking/contests/'+contest.id+'/respond',{revision:contest.revision,action});await this.load(true);return true;}
    catch(error){await this.load(true);this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Unable to update this contest.' : 'Unable to update this contest.');return false;}
    finally{this.busy.set(false);}
  }
  async cancel(proposalRevision:string, reason:string):Promise<boolean> {
    if(this.busy()) return false;
    this.busy.set(true);this.error.set('');
    try {await this.auth.submit('outings/cancel',{proposalRevision,reason});await this.load(true);return true;}
    catch(error) {this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Unable to cancel this date.' : 'Unable to cancel this date.');return false;}
    finally {this.busy.set(false);}
  }
  async reply(proposalRevision:string, kind:'picked'|'surprise', venueToken?:string):Promise<boolean> {
    if(this.busy()) return false;
    this.busy.set(true);this.error.set('');
    try { await this.auth.submit('outings/reply',{proposalRevision,kind,venueToken});await this.load(true);return true; }
    catch(error) { this.error.set(error instanceof HttpErrorResponse ? error.error?.title || 'Unable to send your reply.' : 'Unable to send your reply.');return false; }
    finally {this.busy.set(false);}
  }
  ngOnDestroy(): void { this.disposed=true; clearInterval(this.timer); this.state.set(null); }
}
