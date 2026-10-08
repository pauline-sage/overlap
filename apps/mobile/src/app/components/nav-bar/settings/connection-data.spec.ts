import { TestBed } from '@angular/core/testing';
import { HttpClient } from '@angular/common/http';
import { of } from 'rxjs';
import { ConnectionDataComponent } from './connection-data';
import { AuthService } from '../../../auth.service';

describe('Connection data',()=>{
  beforeEach(()=>sessionStorage.clear());
  it('groups creator archives and keeps the group open after confirmed removal',async()=>{
    const connection={pairId:'a'.repeat(32),partnerName:'Pauline',disconnectedUtc:null,exportExpiresUtc:null};const submit=vi.fn().mockResolvedValue({});
    await TestBed.configureTestingModule({imports:[ConnectionDataComponent],providers:[{provide:HttpClient,useValue:{get:()=>of({creator:true,connections:[connection,{...connection,pairId:'b'.repeat(32)}]})}},{provide:AuthService,useValue:{submit}}]}).compileComponents();
    const fixture=TestBed.createComponent(ConnectionDataComponent);await fixture.whenStable();fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('article').length).toBe(0);
    fixture.nativeElement.querySelector('.archive-disclosure').click();fixture.detectChanges();
    expect(fixture.nativeElement.querySelectorAll('article').length).toBe(2);
    expect(fixture.nativeElement.querySelector('article h4').textContent).toContain('With Pauline');
    fixture.nativeElement.querySelector('.delete-archive').click();fixture.detectChanges();expect(submit).not.toHaveBeenCalled();
    await fixture.componentInstance.remove(connection);fixture.detectChanges();
    expect(submit).toHaveBeenCalledWith(`connection-history/${connection.pairId}/remove`,{confirm:true});
    expect(fixture.componentInstance.expanded()).toBe(true);
    expect(fixture.nativeElement.querySelectorAll('article').length).toBe(1);
  });
  it('offers ordinary users a deadline and download without an archive',async()=>{
    await TestBed.configureTestingModule({imports:[ConnectionDataComponent],providers:[{provide:HttpClient,useValue:{get:()=>of({creator:false,connections:[{pairId:'a'.repeat(32),disconnectedUtc:'2026-10-08T10:00:00Z',exportExpiresUtc:'2026-11-07T10:00:00Z'}]})}}]}).compileComponents();
    const fixture=TestBed.createComponent(ConnectionDataComponent);await fixture.whenStable();fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Download until');
    expect(fixture.nativeElement.textContent).toContain('Download data');
    expect(fixture.nativeElement.textContent).not.toContain('Open my archive');
  });
  it('restores only an authorized creator archive and shows both photos together',async()=>{
    const pair='a'.repeat(32);sessionStorage.setItem('overlap-navigation',JSON.stringify({'archive.pair':pair}));
    const get=vi.fn((url:string)=>of(url.endsWith('/archive')?{drawings:[{id:1,title:'A waffle',photos:[{accountId:'one',mine:true},{accountId:'two',mine:false}]}]}:{creator:true,connections:[{pairId:pair,disconnectedUtc:null,exportExpiresUtc:null}]}));
    await TestBed.configureTestingModule({imports:[ConnectionDataComponent],providers:[{provide:HttpClient,useValue:{get}}]}).compileComponents();
    const fixture=TestBed.createComponent(ConnectionDataComponent);await fixture.whenStable();fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('My archive');
    expect(fixture.nativeElement.textContent).toContain('A waffle');
    expect(fixture.nativeElement.querySelectorAll('img').length).toBe(2);
    fixture.componentInstance.close();fixture.detectChanges();
    expect(JSON.parse(sessionStorage.getItem('overlap-navigation')!)['archive.pair']).toBe('');
  });
});
