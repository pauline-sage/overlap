import { Component, effect, inject, input, output, signal } from '@angular/core';
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
  imports: [FormsModule],
  templateUrl: './shared-time.html',
  styleUrl: './shared-time.scss',
})
export class SharedTimeComponent {
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
  readonly tab = signal<'discover' | 'picks'>('discover');
  private readonly resetDetail = effect(() => {
    if (!this.active()) this.selected.set(null);
  });
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
  togglePick(title: WatchTitle): void {
    this.picks.update((p) =>
      this.saved(title.id) ? p.filter((t) => t.id !== title.id) : [...p, title],
    );
  }
  imageFailed(event: Event): void {
    (event.target as HTMLImageElement).style.display = 'none';
  }
}
