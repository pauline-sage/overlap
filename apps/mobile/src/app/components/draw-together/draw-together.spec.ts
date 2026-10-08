import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { AuthService } from '../../auth.service';
import { DrawTogetherComponent } from './draw-together';
import { By } from '@angular/platform-browser';
import { OutingChoiceComponent } from '../coffee-date/outing-choice/outing-choice';
beforeEach(()=>sessionStorage.removeItem('overlap-navigation'));
afterEach(()=>sessionStorage.removeItem('overlap-navigation'));

describe('Draw together',()=>{
  const auth={submit:vi.fn()};const http={get:vi.fn()};let round:any;
  afterEach(()=>{vi.restoreAllMocks();vi.unstubAllGlobals();});
  beforeEach(()=>{
    vi.clearAllMocks();auth.submit.mockResolvedValue({});
    round={id:1,prompt:'A sleepy dragon',seconds:600,revision:'revision',mine:false,status:'waiting',createdUtc:new Date().toISOString(),startUtc:null,endUtc:null,photos:[]};
    http.get.mockImplementation(()=>of({connected:true,serverUtc:new Date().toISOString(),rounds:[round],categories:[{id:'animals',title:'Animals',count:21},{id:'snk',title:'Attack on Titan / SNK',count:36}]}));
    TestBed.configureTestingModule({imports:[DrawTogetherComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth}]});
  });
  async function render(){const f=TestBed.createComponent(DrawTogetherComponent);f.componentRef.setInput('roundId',1);f.detectChanges();await f.whenStable();f.detectChanges();return f;}
  it('keeps Share focused on photos and stars, with a clear Done action',async()=>{
    round.status='finished';round.seconds=15;
    round.photos=[{accountId:'self',mine:true,revision:'own',uploadedUtc:new Date().toISOString(),myRating:3,partnerRating:4},{accountId:'partner',mine:false,revision:'partner',uploadedUtc:new Date().toISOString(),myRating:null,partnerRating:null}];
    const f=await render();const text=f.nativeElement.textContent;
    expect(text).not.toContain('15 seconds');expect(text).not.toContain('Delete round');expect(text).not.toContain('Remove my photo');
    expect(f.nativeElement.querySelectorAll('app-book-rating .stars button')).toHaveLength(10);
    expect(f.nativeElement.querySelector('.ratings')).toBeNull();
    expect(text).toContain('Your drawings are saved in Albums.');
    const back=vi.fn();f.componentInstance.back.subscribe(back);
    ([...f.nativeElement.querySelectorAll('button')] as HTMLButtonElement[]).find(x=>x.textContent?.trim()==='Done')!.click();
    expect(back).toHaveBeenCalledOnce();expect(f.componentInstance.selected()).toBe(0);f.destroy();
  });
  it('clears a recovered refresh error while preserving action errors',async()=>{
    const f=await render();f.componentInstance.error.set('Your drawings could not be refreshed. Try again.');
    await f.componentInstance.load(true);expect(f.componentInstance.error()).toBe('');
    f.componentInstance.error.set('This round changed. Refresh and try again.');
    await f.componentInstance.load(true);expect(f.componentInstance.error()).toContain('This round changed');f.destroy();
  });
  it('sends selected categories, drawing count and per-round time with the invitation',async()=>{
    http.get.mockImplementation(()=>of({connected:true,serverUtc:new Date().toISOString(),rounds:[],categories:[{id:'animals',title:'Animals',count:21},{id:'snk',title:'Attack on Titan / SNK',count:36}]}));
    const f=TestBed.createComponent(DrawTogetherComponent);f.detectChanges();await f.whenStable();f.detectChanges();
    f.componentInstance.categoryIds.set(['animals']);
    const rail=f.nativeElement.querySelector('.category-carousel');const scrollTo=vi.fn();rail.scrollTo=scrollTo;vi.stubGlobal('matchMedia',vi.fn().mockReturnValue({matches:true}));
    f.nativeElement.querySelector('[aria-label="Next category"]').click();f.detectChanges();expect(f.componentInstance.categoryIndex()).toBe(1);expect(scrollTo).toHaveBeenCalled();
    rail.dispatchEvent(new KeyboardEvent('keydown',{key:'Home',bubbles:true}));f.detectChanges();expect(f.componentInstance.categoryIndex()).toBe(0);
    const snk=f.nativeElement.querySelectorAll('.category-toggle')[1];snk.click();f.detectChanges();
    expect(snk.getAttribute('aria-pressed')).toBe('true');
    const choices=f.debugElement.queryAll(By.directive(OutingChoiceComponent));
    choices[0].componentInstance.valueChange.emit('3');choices[1].componentInstance.valueChange.emit('120');
    await f.whenStable();f.detectChanges();
    auth.submit.mockResolvedValue({id:2});
    await f.componentInstance.create();
    expect(auth.submit).toHaveBeenCalledWith('drawing',{seconds:120,categories:['animals','snk'],drawingCount:3});f.destroy();
  });
  it('offers next drawing within a session and respects its final round',async()=>{
    round.status='finished';round.drawingCount=2;round.drawingNumber=1;round.nextCreated=false;
    const f=await render();expect(f.nativeElement.textContent).toContain('Drawing 1 of 2');
    expect(f.nativeElement.textContent).toContain('Next drawing');
    auth.submit.mockResolvedValue({id:1});await f.componentInstance.nextDrawing();
    expect(auth.submit).toHaveBeenCalledWith('drawing/1/next',{revision:'revision'});
    round.drawingNumber=2;f.componentInstance.rounds.set([{...round}]);f.detectChanges();
    expect(f.componentInstance.canNext(round)).toBe(false);expect(f.nativeElement.textContent).not.toContain('Next drawing');f.destroy();
  });
  it('stops from the overview only after confirmation and returns to editable setup',async()=>{
    const f=TestBed.createComponent(DrawTogetherComponent);f.detectChanges();await f.whenStable();f.detectChanges();
    const buttons=[...f.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    buttons.find(x=>x.getAttribute('aria-label')==='Stop and change setup')!.click();f.detectChanges();
    expect(auth.submit).not.toHaveBeenCalled();expect(f.nativeElement.textContent).toContain('It cannot be resumed');
    auth.submit.mockImplementation(async()=>{round={...round,status:'cancelled'};});
    await f.componentInstance.stopAndChange();f.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith('drawing/1/cancel',{revision:'revision'});
    expect(f.componentInstance.stopRound()).toBeNull();expect(f.nativeElement.textContent).toContain('Time for each drawing');
    expect(f.nativeElement.textContent).toContain('Invite to draw');expect(auth.submit).toHaveBeenCalledTimes(1);f.destroy();
  });
  it('offers continuation instead of a silently disabled setup button',async()=>{
    round.mine=true;
    const f=TestBed.createComponent(DrawTogetherComponent);f.componentRef.setInput('partnerName','André');f.detectChanges();await f.whenStable();f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Waiting for André');
    const buttons=[...f.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    const resume=buttons.find(x=>x.getAttribute('aria-label')==='Continue current round')!;
    expect(resume.disabled).toBe(false);resume.click();f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Shuffle');f.destroy();
  });
  it('pauses with the current revision and resumes saved time after the original deadline',async()=>{
    round.status='drawing';round.startUtc=new Date().toISOString();round.endUtc=new Date(Date.now()+60000).toISOString();
    auth.submit.mockImplementation(async()=>{round.status='paused';round.pausedSeconds=45;round.endUtc=new Date(Date.now()-100000).toISOString();});
    const f=await render();await f.componentInstance.action('pause');
    expect(auth.submit).toHaveBeenCalledWith('drawing/1/pause',{revision:'revision'});
    f.componentInstance.open(round);f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Paused');expect(f.componentInstance.timer(round)).toBe('0:45');
    expect(f.nativeElement.querySelector('input[type=file]')).toBeNull();
    await f.componentInstance.action('resume');expect(auth.submit).toHaveBeenCalledWith('drawing/1/resume',{revision:'revision'});f.destroy();
  });
  it('joins the shared prompt using its current revision',async()=>{
    const f=await render();expect(f.nativeElement.querySelector('[aria-label="Accept invitation and start timer"]')).not.toBeNull();
    await f.componentInstance.action('join');expect(auth.submit).toHaveBeenCalledWith('drawing/1/join',{revision:'revision'});f.destroy();
  });
  it('offers recipient decline and sends the current invitation revision',async()=>{
    const f=await render();const decline=f.nativeElement.querySelector('[aria-label="Decline drawing invitation"]');
    expect(decline).not.toBeNull();decline.click();await f.whenStable();
    expect(auth.submit).toHaveBeenCalledWith('drawing/1/decline',{revision:'revision'});f.destroy();
  });
  it('offers photo upload only after the shared UTC deadline and saves a real selected file',async()=>{
    round.status='drawing';round.endUtc=new Date(Date.now()+60000).toISOString();
    const f=await render();expect(f.nativeElement.querySelector('input[type=file]')).toBeNull();
    round.endUtc=new Date(Date.now()-1000).toISOString();await f.componentInstance.load(true);f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Share your drawing');
    vi.stubGlobal('createImageBitmap',vi.fn().mockResolvedValue({width:512,height:512,close:vi.fn()}));
    vi.spyOn(HTMLCanvasElement.prototype,'getContext').mockReturnValue({fillStyle:'',fillRect:vi.fn(),drawImage:vi.fn(),getImageData:()=>({data:new Uint8ClampedArray(512*512*4)})} as unknown as CanvasRenderingContext2D);
    const file=new File(['pixels'],'drawing.png',{type:'image/png'});
    await f.componentInstance.upload({target:{files:[file],value:'drawing.png'}} as unknown as Event);
    expect(auth.submit).toHaveBeenCalledWith('drawing/1/photo',{revision:'revision',pixels:expect.any(String)});f.destroy();
  });
  it('submits only the current person’s rating for the displayed photo revision',async()=>{
    round.status='finished';round.photos=[{accountId:'partner',revision:'photo-revision',mine:false,myRating:null,partnerRating:4,uploadedUtc:new Date().toISOString()}];
    const f=await render();await f.componentInstance.rating(round.photos[0],5);
    expect(auth.submit).toHaveBeenCalledWith('drawing/1/rating',{revision:'revision',photoAccount:'partner',photoRevision:'photo-revision',score:5});f.destroy();
  });
  it('requires explicit confirmation before removing a shared round',async()=>{
    round.status='finished';const f=await render();f.componentRef.setInput('albumOnly',true);f.detectChanges();const buttons=[...f.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    buttons.find(x=>x.textContent?.trim()==='Delete round')!.click();f.detectChanges();
    expect(auth.submit).not.toHaveBeenCalled();expect(f.nativeElement.textContent).toContain('both photos and ratings');f.destroy();
  });
  it('keeps past history collapsed and deletes an empty round only after confirmation',async()=>{
    round.status='cancelled';
    const f=TestBed.createComponent(DrawTogetherComponent);f.detectChanges();await f.whenStable();f.detectChanges();
    expect(f.nativeElement.querySelector('.round-history').open).toBe(false);
    f.nativeElement.querySelector('.delete-round').click();f.detectChanges();
    expect(auth.submit).not.toHaveBeenCalled();expect(f.nativeElement.textContent).toContain('This cannot be undone');
    http.get.mockImplementation(()=>of({connected:true,serverUtc:new Date().toISOString(),rounds:[]}));
    await f.componentInstance.deleteConfirmed();f.detectChanges();
    expect(auth.submit).toHaveBeenCalledWith('drawing/1/remove',{revision:'revision'});
    expect(f.componentInstance.rounds()).toEqual([]);
    expect(f.nativeElement.querySelector('.round-history').open).toBe(true);f.destroy();
  });
  it('keeps the history page after deletion and steps back only when it becomes empty',async()=>{
    round.status='cancelled';let rounds=Array.from({length:7},(_,i)=>({...round,id:i+1}));
    http.get.mockImplementation(()=>of({connected:true,serverUtc:new Date().toISOString(),rounds}));
    const f=TestBed.createComponent(DrawTogetherComponent);f.detectChanges();await f.whenStable();f.detectChanges();
    f.componentInstance.page.set(2);f.detectChanges();
    for(const id of [6,7]){
      f.componentInstance.requestDelete(rounds.find(x=>x.id===id)!);f.detectChanges();
      auth.submit.mockImplementation(async()=>{rounds=rounds.filter(x=>x.id!==id);});
      await f.componentInstance.deleteConfirmed();f.detectChanges();
      expect(f.nativeElement.querySelector('.round-history').open).toBe(true);
      expect(f.componentInstance.page()).toBe(id===6 ? 2 : 1);
    }
    f.destroy();
  });
  it('preserves photos when clearing Draw history and permits explicit album deletion',async()=>{
    round.status='finished';round.photos=[{accountId:'owner',mine:true,revision:'photo',uploadedUtc:new Date().toISOString()}];
    const f=await render();expect(f.nativeElement.textContent).not.toContain('Delete round');
    f.componentInstance.requestDelete(round);expect(f.componentInstance.deleteRound()).toBeNull();
    f.componentRef.setInput('albumOnly',true);f.detectChanges();
    f.componentInstance.requestDelete(round);expect(f.componentInstance.deleteRound()?.id).toBe(1);
    expect(auth.submit).not.toHaveBeenCalled();f.destroy();
  });
});
