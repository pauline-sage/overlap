import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { AuthService } from '../../auth.service';
import { DrawTogetherComponent } from './draw-together';
describe('Draw together',()=>{
  const auth={submit:vi.fn()};const http={get:vi.fn()};let round:any;
  afterEach(()=>{vi.restoreAllMocks();vi.unstubAllGlobals();});
  beforeEach(()=>{
    vi.clearAllMocks();auth.submit.mockResolvedValue({});
    round={id:1,prompt:'A sleepy dragon',seconds:600,revision:'revision',mine:false,status:'waiting',createdUtc:new Date().toISOString(),startUtc:null,endUtc:null,photos:[]};
    http.get.mockImplementation(()=>of({connected:true,serverUtc:new Date().toISOString(),rounds:[round]}));
    TestBed.configureTestingModule({imports:[DrawTogetherComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth}]});
  });
  async function render(){const f=TestBed.createComponent(DrawTogetherComponent);f.componentRef.setInput('roundId',1);f.detectChanges();await f.whenStable();f.detectChanges();return f;}
  it('joins the shared prompt using its current revision',async()=>{
    const f=await render();expect(f.nativeElement.textContent).toContain('Join and start timer');
    await f.componentInstance.action('join');expect(auth.submit).toHaveBeenCalledWith('drawing/1/join',{revision:'revision'});f.destroy();
  });
  it('offers photo upload only after the shared UTC deadline and saves a real selected file',async()=>{
    round.status='drawing';round.endUtc=new Date(Date.now()+60000).toISOString();
    const f=await render();expect(f.nativeElement.querySelector('input[type=file]')).toBeNull();
    round.endUtc=new Date(Date.now()-1000).toISOString();await f.componentInstance.load(true);f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Time’s up');
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
    round.status='finished';const f=await render();const buttons=[...f.nativeElement.querySelectorAll('button')] as HTMLButtonElement[];
    buttons.find(x=>x.textContent?.trim()==='Remove round from album')!.click();f.detectChanges();
    expect(auth.submit).not.toHaveBeenCalled();expect(f.nativeElement.textContent).toContain('both photos and ratings');f.destroy();
  });
});
