import { TestBed } from '@angular/core/testing';
import { AuthService } from '../../auth.service';
import { BookTitle, ReadTogetherComponent } from './read-together';

const book: BookTitle = {id:'/works/OL1W',title:'A shared story',authors:['An author'],authorIds:['OL1A'],subjects:['fantasy'],coverId:123,year:2020};
describe('Read together',() => {
  it('shows six search results and fills provider boundaries before advancing without skipping books',async()=>{
    auth.submit.mockImplementation((_action,body)=>Promise.resolve({books:Array.from({length:20},(_,i)=>({...book,id:'/works/'+((body.page-1)*20+i),title:'Result '+((body.page-1)*20+i)})),hasMore:body.page===1,explanation:null}));
    const f=await render();const c=f.componentInstance;await c.search();f.detectChanges();
    expect(f.nativeElement.querySelectorAll('.book-card')).toHaveLength(6);
    const next=()=>Array.from(f.nativeElement.querySelectorAll('app-collection-browser button') as NodeListOf<HTMLButtonElement>).find(x=>x.textContent==='Next')!;
    next().click();f.detectChanges();expect(c.displayed()[0].title).toBe('Result 6');
    next().click();f.detectChanges();expect(c.displayed()[0].title).toBe('Result 12');expect(auth.submit).toHaveBeenCalledTimes(1);
    next().click();await vi.waitFor(()=>expect(c.busy()).toBe(false));f.detectChanges();
    expect(c.displayed().map(x=>x.title)).toEqual(['Result 18','Result 19','Result 20','Result 21','Result 22','Result 23']);
    expect(auth.submit).toHaveBeenLastCalledWith('books/search',expect.objectContaining({page:2}));
    next().click();f.detectChanges();expect(c.displayed()[0].title).toBe('Result 24');
    Array.from(f.nativeElement.querySelectorAll('app-collection-browser button') as NodeListOf<HTMLButtonElement>).find(x=>x.textContent==='Previous')!.click();f.detectChanges();expect(c.displayed()[0].title).toBe('Result 18');
    c.changed();expect(c.searchPages.page()).toBe(1);expect(c.results()).toBeNull();f.destroy();
  });
  it('pages saved books six at a time and searches without mixing read books', async () => {
    auth.books.mockResolvedValue({connected:true,entries:[
      ...Array.from({length:20},(_,i)=>({id:20-i,status:'planned',book:{...book,id:'/works/'+i,title:'Book '+i}})),
      {id:21,status:'read',book:{...book,id:'/works/read',title:'Finished book'}},
    ]});
    const f=await render();const c=f.componentInstance;c.view('planned');await f.whenStable();f.detectChanges();
    expect(f.nativeElement.querySelectorAll('.book-card')).toHaveLength(6);
    expect(f.nativeElement.textContent).toContain('Page 1 of 4');
    expect(f.nativeElement.textContent).not.toContain('View all');
    const next=Array.from(f.nativeElement.querySelectorAll('app-collection-browser button') as NodeListOf<HTMLButtonElement>).find(b=>b.textContent==='Next')!;
    next.click();f.detectChanges();expect(c.displayed()[0].title).toBe('Book 6');
    const input=f.nativeElement.querySelector('#shelf-search') as HTMLInputElement;input.value='Book 19';input.dispatchEvent(new Event('input'));f.detectChanges();
    expect(c.collection.page()).toBe(1);expect(c.displayed().map(x=>x.title)).toEqual(['Book 19']);
    c.collection.search('');c.collection.page.set(4);c.shelf.set(c.shelf().slice(0,7));f.detectChanges();
    expect(c.collection.current(c.filteredBooks().length)).toBe(2);expect(c.displayed()).toHaveLength(1);
    c.view('read');await f.whenStable();f.detectChanges();expect(c.collection.page()).toBe(1);expect(c.collection.query()).toBe('');expect(c.displayed().map(x=>x.title)).toEqual(['Finished book']);
    f.destroy();
  });
  it('shows styled empty shelves with a Discover action',async()=>{
    auth.books.mockResolvedValue({connected:true,entries:[]});
    const f=await render();const c=f.componentInstance;
    for(const tab of ['planned','read'] as const){
      c.view(tab);await f.whenStable();f.detectChanges();const empty=f.nativeElement.querySelector('.collection-empty');
      expect(empty).not.toBeNull();expect(empty.querySelector('svg')).not.toBeNull();expect(empty.textContent).toContain(tab==='planned' ? 'Find your first book' : 'Your reading story starts here');
      empty.querySelector('button').click();f.detectChanges();expect(c.tab()).toBe('discover');await f.whenStable();
    }
    auth.submit.mockResolvedValue({books:[],reasons:{},explanation:'Rate a favourite to get recommendations.'});c.view('recommended');await f.whenStable();f.detectChanges();
    const recommended=f.nativeElement.querySelector('.collection-empty');expect(recommended.textContent).toContain('Find your next chapter');expect(recommended.textContent).toContain('Rate a favourite');
    recommended.querySelector('button').click();f.detectChanges();expect(c.tab()).toBe('discover');
    f.destroy();
  });
  const auth={books:vi.fn(),submit:vi.fn()};
  beforeEach(() => {
    vi.resetAllMocks(); auth.books.mockResolvedValue({connected:true,entries:[]});
    auth.submit.mockResolvedValue({books:[book],hasMore:false,explanation:null});
    TestBed.configureTestingModule({imports:[ReadTogetherComponent],providers:[{provide:AuthService,useValue:auth}]});
  });
  async function render(){const f=TestBed.createComponent(ReadTogetherComponent);f.detectChanges();await f.whenStable();return f;}
  it('shows explained recommended books and keeps partner shelf removal owner-only',async () => {
    auth.books.mockResolvedValue({connected:true,sharingBooks:false,entries:[{id:4,status:'planned',book,mine:false}]});
    const f=await render();const c=f.componentInstance;
    c.open(book);await f.whenStable();f.detectChanges();
    expect(f.nativeElement.querySelector('.book-actions').textContent).not.toContain('Remove from bookshelf');
    expect(f.nativeElement.querySelector('.book-actions').textContent).toContain('Add to my bookshelf');
    auth.submit.mockResolvedValue({books:[book],reasons:{[book.id]:'Inspired by a favourite fantasy book.'},explanation:'Based on your ratings.'});
    c.view('recommended');await f.whenStable();f.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith('books/recommended',{});
    expect(f.nativeElement.textContent).toContain('Inspired by a favourite fantasy book.');
    expect(f.nativeElement.querySelectorAll('[role="tab"]').length).toBe(4);
  });
  it('searches real catalogue filters and saves an explicitly shared book',async () => {
    const f=await render(); const c=f.componentInstance;
    c.query='story';c.genre='fantasy';c.language='fr';await c.search();f.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith('books/search',{query:'story',genre:'fantasy',language:'fr',page:1,basedOn:null});
    f.nativeElement.querySelector('.book-card').click();f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Check the edition and language');
    auth.books.mockResolvedValue({connected:true,entries:[{id:4,status:'planned',book}]});
    await c.save(book,'planned');
    expect(auth.submit).toHaveBeenCalledWith('books/shelf',{book,status:'planned'});
    expect(c.entries('planned').length).toBe(1);
    auth.books.mockResolvedValue({connected:true,entries:[{id:4,status:'read',book}]});
    await c.save(book,'read');
    expect(auth.submit).toHaveBeenCalledWith('books/shelf/4/status',{status:'read'});
    c.view('read');f.detectChanges();
    expect(f.nativeElement.querySelectorAll('.book-card').length).toBe(1);
    auth.books.mockResolvedValue({connected:true,entries:[]});await c.remove(book);
    expect(auth.submit).toHaveBeenCalledWith('books/shelf/4/remove',{});
  });
  it('uses a completed book for explainable suggestions and discards stale searches',async () => {
    auth.books.mockResolvedValue({connected:true,entries:[{id:4,status:'read',book}]});
    const f=await render();const c=f.componentInstance;
    c.similar(c.entries('read')[0]);await f.whenStable();
    expect(auth.submit).toHaveBeenCalledWith('books/search',expect.objectContaining({basedOn:4}));
    let finish!:(value:unknown)=>void;
    auth.submit.mockImplementationOnce(() => new Promise(resolve => finish=resolve));
    const pending=c.search();c.query='different';c.changed();
    finish({books:[book],hasMore:false,explanation:null});await pending;
    expect(c.results()).toBeNull();
    c.tabKey(new KeyboardEvent('keydown',{key:'End'}));expect(c.tab()).toBe('recommended');
  });
  it('removes a completed book directly from the list without opening details or restoring it',async () => {
    auth.books.mockResolvedValue({connected:true,entries:[{id:4,status:'read',book}]});
    const f=await render(); const c=f.componentInstance;
    c.view('read');await f.whenStable();f.detectChanges();
    expect(f.nativeElement.querySelector('.book-card').textContent).not.toContain('Read together');
    auth.books.mockResolvedValue({connected:true,entries:[]});
    f.nativeElement.querySelector('.remove-read').click();await f.whenStable();f.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith('books/shelf/4/remove',{});
    expect(c.selected()).toBeNull();expect(c.entries('planned')).toEqual([]);
    expect(f.nativeElement.querySelector('.book-item')).toBeNull();
  });
  it('shows the real summary and only offers moving a completed book back to the bookshelf',async () => {
    auth.books.mockResolvedValue({connected:true,entries:[{id:4,status:'read',book}]});
    auth.submit.mockResolvedValue({description:'A real book summary.'});
    const f=await render(); const c=f.componentInstance;
    c.open(book);await f.whenStable();f.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith('books/details',{id:book.id});
    expect(f.nativeElement.textContent).toContain('A real book summary.');
    expect(f.nativeElement.textContent).toContain('Already read together');
    const actions=f.nativeElement.querySelector('.book-actions');
    expect(actions.querySelectorAll('button').length).toBe(1);
    expect(actions.textContent).toContain('Move to the bookshelf');
    expect(actions.textContent).not.toContain('Find similar books');
  });
  it('saves and clears your own rating directly on a completed book card',async () => {
    auth.books.mockResolvedValue({connected:true,entries:[{id:4,status:'read',book,ownRating:null,partnerRating:3}]});
    const f=await render();const c=f.componentInstance;c.view('read');await f.whenStable();f.detectChanges();
    expect(f.nativeElement.querySelector('app-book-rating').textContent).toContain('Your person: 3/5');
    auth.books.mockResolvedValue({connected:true,entries:[{id:4,status:'read',book,ownRating:5,partnerRating:3}]});
    f.nativeElement.querySelectorAll('app-book-rating .stars button')[4].click();await f.whenStable();f.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith('books/shelf/4/rating',{stars:5});
    expect(c.selected()).toBeNull();
    auth.books.mockResolvedValue({connected:true,entries:[{id:4,status:'read',book,ownRating:null,partnerRating:3}]});
    f.nativeElement.querySelector('app-book-rating .clear').click();await f.whenStable();
    expect(auth.submit).toHaveBeenCalledWith('books/shelf/4/rating',{stars:null});
  });
});
