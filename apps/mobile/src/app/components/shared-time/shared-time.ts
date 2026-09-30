import { LoadingIndicatorComponent } from '../loading-indicator';
import { Component, effect, inject, input, output, signal, untracked, OnDestroy } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../auth.service';
export interface WatchTitle {
  id: string;
  title: string;
  format: string;
  minutes: number | null;
  genres: string[];
  description: string;
  poster: string | null;
  year: string | null;
  source?: 'youtube';
  captions?: boolean;
  channel?: string;
  ownCountry?: string;
  partnerCountry?: string;
  ownServices: string[];
  partnerServices: string[];
  ownLink: string;
  partnerLink: string;
}
interface SharedPick {
  id: number;
  status: 'pending' | 'accepted' | 'declined';
  mine: boolean;
  shared: boolean;
  title: WatchTitle;
}
interface PickList {
  connected: boolean;
  picks: SharedPick[];
}
interface YouTubeVideo {
  id: string;
  title: string;
  channel: string;
  url: string;
  thumbnail: string;
  minutes: number;
  captions: boolean;
}
interface WatchResults {
  titles: WatchTitle[];
  hasMore: boolean;
}
@Component({
  selector: 'app-shared-time',
  imports: [LoadingIndicatorComponent,FormsModule],
  templateUrl: './shared-time.html',
  styleUrl: './shared-time.scss',
})
export class SharedTimeComponent implements OnDestroy {
  private readonly auth = inject(AuthService);
  readonly active = input(false);
  readonly partnerName = input('Your person');
  readonly back = output<void>();
  readonly formats = ['Both', 'Movie', 'Series'];
  readonly genres = [
    'Comedy',
    'Drama',
    'Crime',
    'Mystery',
    'Documentary',
    'Family',
    'Animation',
    'Action & Adventure',
    'Sci-fi & Fantasy',
  ];
  readonly platforms = ['Netflix', 'Prime Video', 'Disney+', 'YouTube'];
  format = 'Both';
  query = '';
  mode = 'title';
  mood = '';
  readonly storyMoods = [
    { value: 'lgbtq', label: 'LGBTQ+' },
    { value: 'bl', label: 'Boys Love (BL)' },
  ];
  minutes = 0;
  ownCountry = 'FR';
  partnerCountry = 'US';
  ownPlatforms = [...this.platforms];
  partnerPlatforms = [...this.platforms];
  readonly results = signal<WatchTitle[] | null>(null);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly hasMore = signal(false);
  readonly selected = signal<WatchTitle | null>(null);
  readonly picks = signal<WatchTitle[]>([]);
  readonly sharedPicks = signal<SharedPick[]>([]);
  readonly connected = signal(false);
  readonly picksReady = signal(false);
  readonly pickBusy = signal(false);
  readonly pickError = signal('');
  private poll?: ReturnType<typeof setInterval>;
  private loadingPicks = false;
  private pickVersion = 0;
  private disposed = false;
  readonly tab = signal<'discover' | 'picks' | 'partner'>('discover');
  private readonly resetDetail = effect(() => {
    clearInterval(this.poll);
    if (!this.active()) this.selected.set(null);
    else {
      untracked(() => void this.loadPicks());
      this.poll = setInterval(() => {
        if (document.visibilityState === 'visible') void this.loadPicks();
      }, 10000);
    }
  });
  pickTitles(mine: boolean): WatchTitle[] {
    return this.picks().filter((_, index) => this.sharedPicks()[index]?.mine === mine);
  }
  view(tab: 'discover' | 'picks' | 'partner'): void {
    this.selected.set(null);
    this.tab.set(tab);
    void this.loadPicks();
  }
  tabKey(event: KeyboardEvent): void {
    const tabs = ['discover', 'picks', 'partner'] as const;
    let index = tabs.indexOf(this.tab());
    if (event.key === 'ArrowRight') index = (index + 1) % tabs.length;
    else if (event.key === 'ArrowLeft') index = (index + tabs.length - 1) % tabs.length;
    else if (event.key === 'Home') index = 0;
    else if (event.key === 'End') index = tabs.length - 1;
    else return;
    event.preventDefault();
    this.view(tabs[index]);
    document.getElementById('watch-tab-' + tabs[index])?.focus();
  }
  private page = 0;
  private searchVersion = 0;
  private opener: HTMLElement | null = null;
  changed(): void {
    this.searchVersion++;
    this.results.set(null);
    this.hasMore.set(false);
    this.error.set('');
  }
  togglePlatform(person: 'you' | 'partner', platform: string): void {
    const values = person === 'you' ? this.ownPlatforms : this.partnerPlatforms;
    const next = values.includes(platform)
      ? values.filter((p) => p !== platform)
      : [...values, platform];
    if (person === 'you') this.ownPlatforms = next;
    else this.partnerPlatforms = next;
    this.changed();
  }
  private message(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) return 'Your session expired. Sign in again.';
      if (error.status === 429) return 'A few searches too quickly. Try again in a minute.';
      return error.error?.title || 'Search is temporarily unavailable.';
    }
    return 'Search is temporarily unavailable.';
  }
  async search(more = false): Promise<void> {
    if (this.busy() || !this.ownPlatforms.length || !this.partnerPlatforms.length) return;
    this.busy.set(true);
    this.error.set('');
    const version = this.searchVersion;
    const page = more ? this.page + 1 : 1;
    const theme = this.storyMoods.some((item) => item.value === this.mood) ? this.mood : '';
    const genre = theme ? '' : this.mood;
    const ownCountry = this.ownCountry,
      partnerCountry = this.partnerCountry;
    if (!more) {
      this.results.set(null);
      this.hasMore.set(false);
    }
    const includeYouTube =
      !more && this.ownPlatforms.includes('YouTube') && this.partnerPlatforms.includes('YouTube');
    try {
      const tasks = [
        this.auth.submit<WatchResults>('watch/search', {
          format: this.format,
          query: this.query,
          mode: this.mode,
          theme,
          origin: '',
          genres: genre ? [genre] : [],
          minutes: Number(this.minutes),
          ownCountry,
          partnerCountry,
          ownPlatforms: this.ownPlatforms,
          partnerPlatforms: this.partnerPlatforms,
          page,
        }),
      ];
      if (includeYouTube)
        tasks.push(
          this.auth
            .submit<YouTubeVideo[]>('watch/youtube', {
              query: this.query,
              theme,
              genre,
              origin: '',
              format: this.format,
              minutes: Number(this.minutes),
              ownCountry,
              partnerCountry,
            })
            .then((videos) => ({
              titles: videos.map((video) => ({
                id: 'youtube-' + video.id,
                title: video.title,
                format: 'YouTube',
                source: 'youtube' as const,
                minutes: video.minutes,
                genres: [],
                description: '',
                poster: video.thumbnail,
                year: null,
                channel: video.channel,
                captions: video.captions,
                ownServices: ['YouTube'],
                partnerServices: ['YouTube'],
                ownLink: video.url,
                partnerLink: video.url,
              })),
              hasMore: false,
            })),
        );
      const responses = await Promise.allSettled(tasks);
      if (version !== this.searchVersion) return;
      const titles: WatchTitle[] = [];
      const warnings: string[] = [];
      responses.forEach((response, index) => {
        if (response.status === 'fulfilled') {
          titles.push(...response.value.titles);
          if (index === 0) {
            this.page = page;
            this.hasMore.set(response.value.hasMore);
          }
        } else
          warnings.push(
            (index === 0 ? 'Catalogue: ' : 'YouTube: ') + this.message(response.reason),
          );
      });
      if (responses.some((response) => response.status === 'fulfilled')) {
        const merged = [
          ...(more ? (this.results() ?? []) : []),
          ...titles.map((title) => ({ ...title, ownCountry, partnerCountry })),
        ];
        this.results.set([...new Map(merged.map((title) => [title.id, title])).values()]);
      }
      this.error.set(warnings.join(' '));
    } finally {
      this.busy.set(false);
    }
  }
  open(title: WatchTitle, event: Event): void {
    this.opener = event.currentTarget as HTMLElement;
    this.selected.set(title);
    setTimeout(() => document.getElementById('watch-detail-title')?.focus());
  }
  close(): void {
    this.selected.set(null);
    setTimeout(() => this.opener?.focus());
  }
  saved(id: string): boolean {
    return this.picks().some((p) => p.id === id);
  }
  ngOnDestroy(): void {
    this.disposed = true;
    clearInterval(this.poll);
  }
  async loadPicks(): Promise<void> {
    if (this.loadingPicks || this.pickBusy() || this.disposed) return;
    this.loadingPicks = true;
    const version = this.pickVersion;
    try {
      const list = await this.auth.picks<PickList>();
      if (version !== this.pickVersion) return;
      this.applyPicks(list);
      this.pickError.set('');
    } catch (error) {
      if(this.tab() === 'partner') this.selected.set(null);
      this.sharedPicks.set([]); this.picks.set([]); this.picksReady.set(false);
      this.pickError.set(this.message(error));
    } finally {
      this.loadingPicks = false;
    }
  }
  private applyPicks(list: PickList): void {
    if (this.disposed) return;
    this.connected.set(list.connected);
    this.picksReady.set(true);
    this.sharedPicks.set(list.picks);
    if(this.tab() === 'partner' && this.selected() && !list.picks.some(p => !p.mine && p.title.id === this.selected()?.id)) this.selected.set(null);
    this.picks.set(
      list.picks.map((p) =>
        p.mine
          ? p.title
          : {
              ...p.title,
              ownCountry: p.title.partnerCountry,
              partnerCountry: p.title.ownCountry,
              ownServices: p.title.partnerServices,
              partnerServices: p.title.ownServices,
              ownLink: p.title.partnerLink,
              partnerLink: p.title.ownLink,
            },
      ),
    );
  }
  pick(id: string): SharedPick | undefined {
    return this.sharedPicks().find((p) => p.title.id === id && p.mine === (this.tab() !== 'partner'));
  }
  async sharePick(title: WatchTitle, shared: boolean): Promise<void> {
    const pick = this.pick(title.id);
    if(pick?.mine) await this.changePick('watch/picks/'+pick.id+'/share', {shared});
  }
  async togglePick(title: WatchTitle): Promise<void> {
    const pick = this.pick(title.id);
    if (pick && !pick.mine) return;
    await this.changePick(
      pick ? 'watch/picks/' + pick.id + '/remove' : 'watch/picks',
      pick ? {} : title,
    );
  }
  async decide(id: string, status: 'accepted' | 'declined'): Promise<void> {
    const pick = this.pick(id);
    if (pick && !pick.mine && this.tab() === 'partner')
      await this.changePick('watch/picks/' + pick.id + '/decision', { status });
  }
  private async changePick(action: string, body: object): Promise<void> {
    if (this.pickBusy()) return;
    this.pickVersion++;
    this.pickBusy.set(true);
    this.pickError.set('');
    try {
      await this.auth.submit(action, body);
      this.applyPicks(await this.auth.picks<PickList>());
    } catch (error) {
      this.pickError.set(this.message(error));
    } finally {
      this.pickBusy.set(false);
    }
  }
  imageFailed(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }
}
