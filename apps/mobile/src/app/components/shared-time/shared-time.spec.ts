import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from '../../auth.service';
import { SharedTimeComponent, WatchTitle } from './shared-time';
const title: WatchTitle = {
  id: 'movie-1',
  title: 'A real result',
  format: 'Movie',
  minutes: 90,
  description: 'The full summary.',
  poster: 'https://image.tmdb.org/t/p/w500/example.jpg',
  year: '2025',
  genres: ['Comedy'],
  ownServices: ['Netflix'],
  partnerServices: ['Prime Video'],
  ownLink: 'https://www.themoviedb.org/movie/1',
  partnerLink: 'https://www.themoviedb.org/movie/1',
};
const video = {
  id: 'abcdefghijk',
  title: 'Episode 1 [1/4]',
  channel: 'Channel',
  minutes: 25,
  captions: true,
  url: 'https://www.youtube.com/watch?v=abcdefghijk',
  thumbnail: 'https://i.ytimg.com/vi/abcdefghijk/hqdefault.jpg',
};
describe('Unified watch search', () => {
  const auth = { submit: vi.fn(), picks: vi.fn() };
  beforeEach(() => {
    vi.resetAllMocks();
    auth.picks.mockResolvedValue({ connected: true, picks: [] });
    auth.submit.mockImplementation((action: string) =>
      Promise.resolve(action === 'watch/youtube' ? [video] : { titles: [title], hasMore: true }),
    );
    TestBed.configureTestingModule({
      imports: [SharedTimeComponent],
      providers: [{ provide: AuthService, useValue: auth }],
    });
  });
  function render() {
    const fixture = TestBed.createComponent(SharedTimeComponent);
    fixture.componentRef.setInput('active', true);
    fixture.detectChanges();
    return fixture;
  }
  it('puts LGBTQ+ and BL in the mood and removes the separate controls', () => {
    const f = render();
    const options = f.nativeElement.querySelector('#watch-genre').textContent;
    expect(options).toContain('LGBTQ+');
    expect(options).toContain('Boys Love');
    expect(options).not.toContain('Yaoi');
    expect(f.nativeElement.textContent).not.toContain('Stories & origins');
    expect(f.nativeElement.textContent).not.toContain('Explore YouTube episodes');
    expect(f.nativeElement.querySelector('#watch-origin')).toBeNull();
  });
  it('searches both sources with the same mood and time in one action', async () => {
    const f = render();
    const c = f.componentInstance;
    c.mood = 'bl';
    c.minutes = 30;
    await c.search();
    f.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith(
      'watch/search',
      expect.objectContaining({ theme: 'bl', genres: [], origin: '', minutes: 30 }),
    );
    expect(auth.submit).toHaveBeenCalledWith(
      'watch/youtube',
      expect.objectContaining({ theme: 'bl', genre: '', origin: '', minutes: 30 }),
    );
    expect(f.nativeElement.querySelectorAll('.title-card').length).toBe(2);
    f.nativeElement.querySelectorAll('.title-card')[1].click();
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain('language unverified');
    expect(f.nativeElement.textContent).not.toContain('Availability by JustWatch');
    auth.picks.mockResolvedValue({
      connected: true,
      picks: [{ id: 1, status: 'pending', mine: true, title: c.selected()! }],
    });
    await c.togglePick(c.selected()!);
    expect(c.picks()[0].source).toBe('youtube');
  });
  it('passes ordinary genres to both searches and displays summaries on click', async () => {
    const f = render();
    const c = f.componentInstance;
    c.mood = 'Comedy';
    await c.search();
    f.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith(
      'watch/youtube',
      expect.objectContaining({ genre: 'Comedy', theme: '' }),
    );
    expect(f.nativeElement.textContent).not.toContain(title.description);
    f.nativeElement.querySelector('.title-card').click();
    f.detectChanges();
    expect(f.nativeElement.textContent).toContain(title.description);
  });
  it('does not search YouTube if either participant deselects it', async () => {
    const c = render().componentInstance;
    c.partnerPlatforms = ['Netflix'];
    await c.search();
    expect(auth.submit).toHaveBeenCalledTimes(1);
    expect(c.results()?.length).toBe(1);
  });
  it('keeps successful results when the other source fails', async () => {
    auth.submit.mockImplementation((action: string) =>
      action === 'watch/youtube'
        ? Promise.reject(new HttpErrorResponse({ status: 502 }))
        : Promise.resolve({ titles: [title], hasMore: false }),
    );
    const c = render().componentInstance;
    await c.search();
    expect(c.results()).toHaveLength(1);
    expect(c.error()).toContain('YouTube');
  });
  it('retains YouTube results when the catalogue fails', async () => {
    auth.submit.mockImplementation((action: string) =>
      action === 'watch/search'
        ? Promise.reject(new HttpErrorResponse({ status: 502 }))
        : Promise.resolve([video]),
    );
    const c = render().componentInstance;
    await c.search();
    expect(c.results()?.[0].source).toBe('youtube');
    expect(c.error()).toContain('Catalogue');
  });
  it('discards late results after a filter change', async () => {
    let complete!: (value: unknown) => void;
    auth.submit.mockImplementation((action: string) =>
      action === 'watch/youtube' ? Promise.resolve([video]) : new Promise((r) => (complete = r)),
    );
    const c = render().componentInstance;
    const pending = c.search();
    c.changed();
    complete({ titles: [title], hasMore: true });
    await pending;
    expect(c.results()).toBeNull();
  });
  it('retains videos on pagination without rerunning YouTube or duplicating titles', async () => {
    const c = render().componentInstance;
    await c.search();
    await c.search(true);
    expect(c.results()).toHaveLength(2);
    expect(auth.submit.mock.calls.filter((call) => call[0] === 'watch/youtube')).toHaveLength(1);
    expect(auth.submit).toHaveBeenLastCalledWith(
      'watch/search',
      expect.objectContaining({ page: 2 }),
    );
  });
  it('separates proposals and swaps country and service labels for the recipient', async () => {
    auth.picks.mockResolvedValue({
      connected: true,
      picks: [
        { id: 1, mine: true, status: 'pending', title: { ...title, id: 'own' } },
        {
          id: 2,
          mine: false,
          status: 'pending',
          title: { ...title, id: 'partner', ownCountry: 'US', partnerCountry: 'FR' },
        },
      ],
    });
    const f = render();
    await f.whenStable();
    const c = f.componentInstance;
    expect(c.pickTitles(true).map((t) => t.id)).toEqual(['own']);
    expect(c.pickTitles(false)[0].ownCountry).toBe('FR');
    expect(c.pickTitles(false)[0].ownServices).toEqual(['Prime Video']);
    auth.submit.mockResolvedValue({});
    await c.decide('partner', 'accepted');
    expect(auth.submit).not.toHaveBeenCalled();
    c.tab.set('partner');
    await c.decide('partner', 'accepted');
    expect(auth.submit).toHaveBeenCalledWith('watch/picks/2/decision', { status: 'accepted' });
    auth.submit.mockClear();
    await c.decide('own', 'accepted');
    expect(auth.submit).not.toHaveBeenCalled();
  });
});
