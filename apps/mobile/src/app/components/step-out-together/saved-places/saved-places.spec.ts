import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { AuthService } from '../../../auth.service';
import { SavedPlacesComponent } from './saved-places';
it('shows five places per page and searches the full collection',async()=>{
 const items=Array.from({length:12},(_,i)=>({id:i+1,name:'Place '+i,location:'City',link:null,note:null,revision:'r'+i,mine:true}));
 const http={get:vi.fn().mockReturnValue(of({connected:true,items}))};
 TestBed.configureTestingModule({imports:[SavedPlacesComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:{submit:vi.fn()}}]});
 const f=TestBed.createComponent(SavedPlacesComponent);f.detectChanges();await vi.waitFor(()=>expect(f.componentInstance.busy()).toBe(false));f.detectChanges();const c=f.componentInstance;
 expect(c.visible()).toHaveLength(5);expect(f.nativeElement.textContent).toContain('1 / 3');
 const next=Array.from(f.nativeElement.querySelectorAll('nav button') as NodeListOf<HTMLButtonElement>).find(b=>b.getAttribute('aria-label')==='Next page')!;
 next.click();f.detectChanges();expect(c.visible()[0].name).toBe('Place 5');expect(f.nativeElement.textContent).toContain('2 / 3');
 next.click();f.detectChanges();expect(c.visible()).toHaveLength(2);expect(next.disabled).toBe(true);
 c.search('Place 11');f.detectChanges();expect(c.page()).toBe(0);expect(c.visible().map(x=>x.name)).toEqual(['Place 11']);f.destroy();
});
it('saves a shared idea and requires confirmation before removing it',async()=>{
 const place={id:1,name:'Museum',location:'New York',link:null,note:'Dinosaurs',revision:'revision',mine:true};
 const http={get:vi.fn().mockReturnValue(of({connected:true,items:[place]}))};const auth={submit:vi.fn().mockResolvedValue({})};
 TestBed.configureTestingModule({imports:[SavedPlacesComponent],providers:[{provide:HttpClient,useValue:http},{provide:AuthService,useValue:auth}]});
 const f=TestBed.createComponent(SavedPlacesComponent);f.detectChanges();await vi.waitFor(()=>expect(f.componentInstance.busy()).toBe(false));const c=f.componentInstance;
 c.open();c.name='Beach';c.location='Minas Gerais, Brazil';await c.save();expect(auth.submit).toHaveBeenCalledWith('saved-places',{name:'Beach',location:'Minas Gerais, Brazil',link:'',note:'',revision:undefined,selectionToken:undefined,clearIdentity:false});expect(c.formOpen()).toBe(false);
 auth.submit.mockClear();await c.remove(place);expect(auth.submit).not.toHaveBeenCalled();c.removing.set(place.id);await c.remove(place);expect(auth.submit).toHaveBeenCalledWith('saved-places/1/remove',{revision:'revision'});f.destroy();
});
