import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { AuthService } from '../../../auth.service';
import { CookingMemoriesComponent } from './cooking-memories';
const recipe={title:'Feijoada',ingredients:['Beans'],instructions:['Simmer'],sourceUrl:'https://en.wikibooks.org'};
const entry={contestId:7,revision:'current',startUtc:'2026-10-05T08:00:00Z',completed:false,archived:false,recipe,photos:[] as {mine:boolean;accountId:string;revision:string;uploadedUtc:string}[]};
describe('Cooking memories',()=>{
  afterEach(()=>vi.restoreAllMocks());
  it('prompts for camera or upload, then saves only the caller?s dish and waits for the partner',async()=>{
    let current={...entry};
    const http={get:vi.fn(()=>of(current))};const auth={submit:vi.fn(async()=>{current={...current,archived:true,revision:'saved',photos:[{mine:true,accountId:'owner',revision:'photo',uploadedUtc:'2026-10-05T09:00:00Z'}]};})};
    TestBed.configureTestingModule({imports:[CookingMemoriesComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth}]});
    const f=TestBed.createComponent(CookingMemoriesComponent);f.componentRef.setInput('contestId',7);f.detectChanges();await f.whenStable();f.detectChanges();
    const finish=f.nativeElement.querySelector('.primary');expect(finish.textContent).toContain('finished cooking');finish.click();f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Take a photo');expect(f.nativeElement.textContent).toContain('Upload a photo');
    const camera=f.nativeElement.querySelector('input[capture]');expect(camera.getAttribute('capture')).toBe('environment');
    const bitmap={width:1024,height:768,close:vi.fn()};vi.stubGlobal('createImageBitmap',vi.fn().mockResolvedValue(bitmap));
    vi.spyOn(HTMLCanvasElement.prototype,'getContext').mockReturnValue({fillStyle:'',fillRect:vi.fn(),drawImage:vi.fn(),getImageData:()=>({data:new Uint8ClampedArray(512*512*4)})} as unknown as CanvasRenderingContext2D);
    try{
      Object.defineProperty(camera,'files',{value:[new File(['image'],'dish.jpg',{type:'image/jpeg'})]});await f.componentInstance.upload({target:camera} as unknown as Event);f.detectChanges();
      expect(auth.submit).toHaveBeenCalledOnce();expect(auth.submit.mock.calls[0]).toEqual(['cooking/memories/7/photo',{revision:'current',pixels:expect.any(String)}]);
      expect(f.nativeElement.textContent).toContain('Your partner can finish');expect(f.nativeElement.querySelectorAll('img')).toHaveLength(1);expect(bitmap.close).toHaveBeenCalledOnce();
    }finally{f.destroy();vi.unstubAllGlobals();}
  });
  it('opens retained recipes in the album and removes only the caller?s photo',async()=>{
    let current={...entry,archived:true,completed:true,photos:[{mine:true,accountId:'owner',revision:'own',uploadedUtc:'2026-10-05T09:00:00Z'},{mine:false,accountId:'partner',revision:'partner',uploadedUtc:'2026-10-05T09:01:00Z'}]};
    const http={get:vi.fn(()=>of([current]))};const auth={submit:vi.fn(async()=>{current={...current,completed:false,photos:current.photos.filter(x=>!x.mine)};})};
    TestBed.configureTestingModule({imports:[CookingMemoriesComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth}]});
    const f=TestBed.createComponent(CookingMemoriesComponent);f.detectChanges();await f.whenStable();f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Our recipe album');f.nativeElement.querySelector('.recipe-row').click();f.detectChanges();
    expect(f.nativeElement.textContent).toContain('Beans');expect(f.nativeElement.textContent).toContain('Simmer');expect(f.nativeElement.querySelectorAll('img')).toHaveLength(2);
    await f.componentInstance.removePhoto();f.detectChanges();expect(auth.submit).toHaveBeenCalledExactlyOnceWith('cooking/memories/7/photo',{revision:'current',pixels:null});expect(f.nativeElement.querySelectorAll('img')).toHaveLength(1);expect(f.nativeElement.querySelector('img').getAttribute('src')).toContain('/photos/partner');f.destroy();
  });
});
