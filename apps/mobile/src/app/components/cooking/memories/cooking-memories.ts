import { Component, DestroyRef, effect, inject, input, output, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../../auth.service';
import { LoadingIndicatorComponent } from '../../loading-indicator/loading-indicator';
interface DishPhoto {mine:boolean;accountId:string;revision:string;uploadedUtc:string;}
interface Memory {contestId:number;revision:string;startUtc:string;completed:boolean;archived:boolean;recipe:{title:string;ingredients:string[];instructions:string[];sourceUrl:string;};photos:DishPhoto[];}
@Component({selector:'app-cooking-memories',imports:[DatePipe,LoadingIndicatorComponent],templateUrl:'./cooking-memories.html',styleUrls:['../cooking.scss','./cooking-memories.scss']})
export class CookingMemoriesComponent {
  readonly contestId=input(0);readonly back=output<void>();readonly archivedChange=output<boolean>();
  readonly entries=signal<Memory[]>([]);readonly selected=signal<Memory|null>(null);
  readonly busy=signal(false);readonly error=signal('');readonly notice=signal('');readonly finishing=signal(false);
  private readonly http=inject(HttpClient);private readonly auth=inject(AuthService);
  private version=0;private loading=false;private destroyed=false;
  constructor(){
    effect(()=>{this.contestId();this.selected.set(null);this.finishing.set(false);void this.load(true);});
    const refresh=()=>{if(document.visibilityState==='visible')void this.load();};
    const timer=setInterval(refresh,10000);document.addEventListener('visibilitychange',refresh);
    inject(DestroyRef).onDestroy(()=>{this.destroyed=true;++this.version;clearInterval(timer);document.removeEventListener('visibilitychange',refresh);});
  }
  async load(force=false):Promise<void>{
    if(!force && (this.loading || this.busy()))return;
    this.loading=true;const version=++this.version;
    try{
      if(this.contestId()){
        const entry=await firstValueFrom(this.http.get<Memory>('/api/auth/cooking/memories/'+this.contestId()));
        if(!this.destroyed && version===this.version){this.selected.set(entry);this.archivedChange.emit(entry.archived);}
      }else{
        const entries=await firstValueFrom(this.http.get<Memory[]>('/api/auth/cooking/memories'));
        if(!this.destroyed && version===this.version){this.entries.set(entries);const id=this.selected()?.contestId;if(id)this.selected.set(entries.find(x=>x.contestId===id) ?? null);}
      }
    }catch{if(!this.destroyed && version===this.version)this.error.set('Your cooking memories could not be refreshed. Try again.');}
    finally{if(version===this.version)this.loading=false;}
  }
  open(entry:Memory):void {this.selected.set(entry);this.finishing.set(false);this.error.set('');this.notice.set('');}
  ownPhoto():DishPhoto|undefined{return this.selected()?.photos.find(x=>x.mine);}
  photoUrl(photo:DishPhoto):string{return '/api/auth/cooking/memories/'+this.selected()!.contestId+'/photos/'+encodeURIComponent(photo.accountId)+'?v='+photo.revision;}
  async upload(event:Event):Promise<void>{
    const input=event.target as HTMLInputElement;const file=input.files?.[0];input.value='';
    if(!file || this.busy() || !this.selected())return;
    if(!['image/jpeg','image/png','image/webp'].includes(file.type) || file.size>10*1024*1024){this.error.set('Choose a JPEG, PNG or WebP photo smaller than 10 MB.');return;}
    const entry=this.selected()!;
    this.busy.set(true);this.error.set('');this.notice.set('');++this.version;
    try{
      const bitmap=await createImageBitmap(file);
      let pixels:string;
      try{
        if(bitmap.width*bitmap.height>40000000)throw new Error('Photo too large');
        const canvas=document.createElement('canvas');canvas.width=canvas.height=512;
        const context=canvas.getContext('2d');if(!context)throw new Error('Canvas unavailable');
        context.fillStyle='#ffffff';context.fillRect(0,0,512,512);
        const scale=Math.min(512/bitmap.width,512/bitmap.height);const width=bitmap.width*scale,height=bitmap.height*scale;
        context.drawImage(bitmap,(512-width)/2,(512-height)/2,width,height);
        const rgba=context.getImageData(0,0,512,512).data;const rgb=new Uint8Array(512*512*3);
        for(let i=0,j=0;i<rgba.length;i+=4){rgb[j++]=rgba[i];rgb[j++]=rgba[i+1];rgb[j++]=rgba[i+2];}
        let binary='';for(let i=0;i<rgb.length;i+=8192)binary+=String.fromCharCode(...rgb.subarray(i,i+8192));pixels=btoa(binary);
      }finally{bitmap.close();}
      await this.auth.submit('cooking/memories/'+entry.contestId+'/photo',{revision:entry.revision,pixels});
      this.notice.set('Your dish photo is saved in your shared recipe album.');this.finishing.set(false);
    }catch(error){this.problem(error,'Your photo could not be saved. Choose a supported image and try again.');}
    finally{await this.load(true);this.busy.set(false);}
  }
  async removePhoto():Promise<void>{
    const entry=this.selected();if(this.busy() || !entry || !this.ownPhoto())return;
    this.busy.set(true);this.error.set('');this.notice.set('');++this.version;
    try{await this.auth.submit('cooking/memories/'+entry.contestId+'/photo',{revision:entry.revision,pixels:null});this.notice.set('Your photo was removed. The recipe and your partner’s photo stay in the album.');}
    catch(error){this.problem(error,'Your photo could not be removed. Try again.');}
    finally{await this.load(true);this.busy.set(false);}
  }
  private problem(error:unknown,fallback:string):void{if(!this.destroyed)this.error.set(error instanceof HttpErrorResponse ? error.error?.title || fallback : fallback);}
}
