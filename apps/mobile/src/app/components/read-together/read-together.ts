import { NavigationStateService, oneOf, navigationId, navigationBoolean } from '../../navigation-state.service';
import { Component, inject, OnDestroy, OnInit, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../auth.service';
import { LoadingIndicatorComponent } from '../loading-indicator/loading-indicator';
import { BookRatingComponent } from './book-rating/book-rating';
import { CollectionBrowser, CollectionBrowserComponent } from '../collection-browser/collection-browser';

export interface BookTitle {
  id: string; title: string; authors: string[]; authorIds: string[]; subjects: string[];
  coverId: number | null; year: number | null;
}
interface ReadingEntry { id: number; status: 'planned' | 'read'; book: BookTitle; mine?: boolean; ownRating?: number | null; partnerRating?: number | null; }
interface Shelf { connected: boolean; entries: ReadingEntry[]; sharingBooks?: boolean; }
interface BookResults { books: BookTitle[]; hasMore: boolean; explanation: string | null; }

@Component({
  selector: 'app-read-together', imports: [FormsModule, LoadingIndicatorComponent, BookRatingComponent, CollectionBrowserComponent],
  templateUrl: './read-together.html', styleUrl: './read-together.scss',
})
export class ReadTogetherComponent implements OnInit, OnDestroy {
  private readonly navigation=inject(NavigationStateService);
  readonly collection = new CollectionBrowser(6);
  readonly searchPages = new CollectionBrowser(6);
  async nextSearchPage():Promise<void> {
    const count=this.results()?.length ?? 0;const version=this.searchVersion;
    const page=this.searchPages.current(this.results()?.length ?? 0);
    await this.search(true);
    if(version===this.searchVersion && count>=this.searchPages.size && this.searchPages.pages(this.results()?.length ?? 0)>page) this.searchPages.page.set(page+1);
  }
  private readonly auth = inject(AuthService);
  readonly back = output<void>();
  readonly tab = this.navigation.remember<'discover'|'planned'|'read'|'recommended'>('read.tab',oneOf(['discover','planned','read','recommended']),'discover');
  readonly recommendations = signal<BookTitle[]>([]);
  readonly recommendationReasons = signal<Record<string,string>>({});
  readonly recommendationExplanation = signal('');
  readonly recommendationError = signal('');
  readonly recommending = signal(false);
  readonly sharingBooks = signal(false);
  readonly shelf = signal<ReadingEntry[]>([]);
  readonly connected = signal(false);
  readonly ready = signal(false);
  readonly results = signal<BookTitle[] | null>(null);
  readonly selected = signal<BookTitle | null>(null);
  readonly summary = signal<string | null>(null);
  readonly summaryBusy = signal(false);
  readonly summaryError = signal('');
  readonly busy = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly shelfError = signal('');
  readonly hasMore = signal(false);
  readonly explanation = signal('');
  readonly genres = ['Fiction', 'Romance', 'Fantasy', 'Science fiction', 'Mystery', 'Thriller', 'Horror', 'Historical fiction', 'Biography', 'Poetry'];
  query = ''; genre = ''; language = ''; basedOn: number | null = null;
  private page = 1;
  private searchVersion = 0;
  private shelfVersion = 0;
  private loading = false;
  private disposed = false;
  private poll?: ReturnType<typeof setInterval>;
  ngOnInit(): void {
    void this.loadShelf();if(this.tab()==='recommended')void this.recommend();
    this.poll = setInterval(() => { if(document.visibilityState === 'visible') void this.loadShelf(); },10000);
  }
  ngOnDestroy(): void { this.disposed = true; clearInterval(this.poll); }
  view(tab: 'discover' | 'planned' | 'read' | 'recommended'): void { this.selected.set(null); this.collection.reset(); this.tab.set(tab); void this.loadShelf(); if(tab === 'recommended') void this.recommend(); }
  tabKey(event: KeyboardEvent): void {
    const tabs = ['discover','planned','read','recommended'] as const;
    let index = tabs.indexOf(this.tab());
    if(event.key === 'ArrowRight') index = (index+1)%tabs.length;
    else if(event.key === 'ArrowLeft') index = (index+tabs.length-1)%tabs.length;
    else if(event.key === 'Home') index = 0;
    else if(event.key === 'End') index = tabs.length-1;
    else return;
    event.preventDefault(); this.view(tabs[index]); document.getElementById('read-tab-'+tabs[index])?.focus();
  }
  entries(status: 'planned' | 'read'): ReadingEntry[] { return this.shelf().filter(x => x.status === status); }
  savedBooks(): BookTitle[] { return [...new Map(this.entries(this.tab() === 'read' ? 'read' : 'planned').map(x => [x.book.id,x.book])).values()]; }
  filteredBooks(): BookTitle[] { return this.savedBooks().filter(book => this.collection.matches(book.title+' '+book.authors.join(' '))); }
  displayed(): BookTitle[] { return this.tab() === 'discover' ? this.searchPages.slice(this.results() ?? []) : this.tab() === 'recommended' ? this.searchPages.slice(this.recommendations()) : this.collection.slice(this.filteredBooks()); }
  entry(id: string): ReadingEntry | undefined { const entries=this.shelf().filter(x => x.book.id === id); return entries.find(x => x.status === 'read') ?? entries.find(x => x.mine !== false) ?? entries[0]; }
  async recommend(): Promise<void> {
    if(this.recommending()) return;
    this.recommending.set(true); this.recommendationError.set('');
    try {
      const result=await this.auth.submit<BookResults & {reasons:Record<string,string>}>('books/recommended',{});
      if(this.disposed) return;
      this.recommendations.set(result.books); this.recommendationReasons.set(result.reasons); this.recommendationExplanation.set(result.explanation ?? '');
    } catch(error) { if(!this.disposed) this.recommendationError.set(this.message(error)); }
    finally { this.recommending.set(false); }
  }
  changed(): void { this.searchPages.reset(); this.searchVersion++; this.results.set(null); this.hasMore.set(false); this.explanation.set(''); this.error.set(''); }
  async search(more = false): Promise<void> {
    if(this.busy()) return;
    if (document.activeElement instanceof HTMLInputElement) document.activeElement.blur();
    this.busy.set(true); this.error.set('');
    const version = this.searchVersion; const page = more ? this.page+1 : 1;
    if(!more) { this.searchPages.reset(); this.results.set(null); }
    try {
      const results = await this.auth.submit<BookResults>('books/search',{query:this.query,genre:this.genre,language:this.language,page,basedOn:this.basedOn});
      if(this.disposed || version !== this.searchVersion) return;
      this.page = page;
      this.results.set([...new Map([...(more ? this.results() ?? [] : []),...results.books].map(x => [x.id,x])).values()]);
      this.hasMore.set(results.hasMore); this.explanation.set(results.explanation ?? '');
    } catch(error) { if(version === this.searchVersion && !this.disposed) this.error.set(this.message(error)); }
    finally { this.busy.set(false); }
  }
  open(book: BookTitle): void {
    this.selected.set(book); this.summary.set(null); this.summaryError.set('');
    queueMicrotask(() => document.getElementById('read-detail-heading')?.focus());
    void this.loadSummary(book);
  }
  private summaryVersion = 0;
  private async loadSummary(book: BookTitle): Promise<void> {
    const version = ++this.summaryVersion;
    this.summaryBusy.set(true);
    try {
      const result = await this.auth.submit<{description: string | null}>('books/details', {id:book.id});
      if (!this.disposed && version === this.summaryVersion && this.selected()?.id === book.id) this.summary.set(result.description ?? null);
    } catch (error) {
      if (!this.disposed && version === this.summaryVersion && this.selected()?.id === book.id) this.summaryError.set(this.message(error));
    } finally { if (version === this.summaryVersion) this.summaryBusy.set(false); }
  }
  similar(entry: ReadingEntry): void { this.basedOn=entry.id; this.query=''; this.genre=''; this.view('discover'); this.changed(); void this.search(); }
  async loadShelf(): Promise<void> {
    if(this.loading || this.saving() || this.disposed) return;
    this.loading=true; const version=this.shelfVersion;
    try {
      const shelf = await this.auth.books<Shelf>();
      if(version === this.shelfVersion) this.apply(shelf);
    } catch(error) {
      if(version !== this.shelfVersion || this.disposed) return;
      this.shelf.set([]); this.ready.set(false); this.connected.set(false);
      if(this.tab() !== 'discover') this.selected.set(null);
      this.shelfError.set(this.message(error));
    } finally { this.loading=false; }
  }
  private apply(shelf: Shelf): void {
    if(this.disposed) return;
    this.shelf.set(shelf.entries); this.sharingBooks.set(shelf.sharingBooks ?? false); this.connected.set(shelf.connected); this.ready.set(true); this.shelfError.set('');
    if(this.basedOn && !shelf.entries.some(x => x.id === this.basedOn && x.status === 'read')) { this.basedOn=null; this.changed(); }
    if(this.tab() !== 'discover' && this.tab() !== 'recommended' && this.selected() && !this.entry(this.selected()!.id)) this.selected.set(null);
  }
  async save(book: BookTitle, status: 'planned' | 'read'): Promise<void> {
    const entry=this.entry(book.id);
    const existing=entry && !(entry.status === 'planned' && entry.mine === false && status === 'planned');
    await this.change(existing ? 'books/shelf/'+entry.id+'/status' : 'books/shelf',existing ? {status} : {book,status});
  }
  async remove(book: BookTitle): Promise<void> { const entry=this.entry(book.id); if(entry) await this.change('books/shelf/'+entry.id+'/remove',{}); }
  async rate(book: BookTitle, stars: number | null): Promise<void> {
    const entry=this.entry(book.id);
    if(entry?.status === 'read') await this.change('books/shelf/'+entry.id+'/rating',{stars});
  }
  private async change(action: string, body: object): Promise<void> {
    if(this.saving()) return;
    this.saving.set(true); this.shelfVersion++; this.shelfError.set('');
    try { await this.auth.submit(action,body); this.apply(await this.auth.books<Shelf>()); }
    catch(error) { this.shelfError.set(this.message(error)); }
    finally { this.saving.set(false); }
  }
  private message(error: unknown): string {
    if(error instanceof HttpErrorResponse) return error.error?.title || (error.status === 401 ? 'Your session expired. Sign in again.' : error.status === 429 ? 'Please wait a minute before trying again.' : 'Books are unavailable right now. Please try again.');
    return 'Unable to complete this action. Please try again.';
  }
  imageFailed(event: Event): void { (event.target as HTMLImageElement).style.display='none'; }
}
