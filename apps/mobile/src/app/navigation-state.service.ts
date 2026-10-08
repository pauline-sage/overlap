import { Injectable, effect, signal, untracked } from '@angular/core';

/** Per-tab navigation only. Never pass drafts, credentials, tokens or locations here. */
@Injectable({providedIn:'root'})
export class NavigationStateService {
  private readonly key='overlap-navigation';
  private values:Record<string,unknown>={};
  constructor(){
    try{const saved=JSON.parse(sessionStorage.getItem(this.key) ?? '{}');if(saved && typeof saved==='object' && !Array.isArray(saved))this.values=saved;}
    catch{/* Navigation still works when storage is unavailable. */}
  }
  read<T extends string|number|boolean>(key:string,valid:(value:unknown)=>value is T,fallback:T):T{
    const value=this.values[key];return valid(value) ? value : fallback;
  }
  write(key:string,value:string|number|boolean):void{
    this.values[key]=value;
    try{sessionStorage.setItem(this.key,JSON.stringify(this.values));}catch{/* Keep navigation in memory. */}
  }
  remember<T extends string|number|boolean>(key:string,valid:(value:unknown)=>value is T,fallback:T){
    const state=signal(this.read(key,valid,fallback));
    effect(()=>{const value=state();untracked(()=>this.write(key,value));});
    return state;
  }
  clear():void{this.values={};try{sessionStorage.removeItem(this.key);}catch{/* Storage may be unavailable. */}}
}

export function oneOf<T extends string>(values:readonly T[]){return (value:unknown):value is T=>typeof value==='string' && values.includes(value as T);}
export const navigationId=(value:unknown):value is number=>typeof value==='number' && Number.isSafeInteger(value) && value>=0;
export const navigationBoolean=(value:unknown):value is boolean=>typeof value==='boolean';
