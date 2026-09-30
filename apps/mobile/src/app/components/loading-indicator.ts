import { Component, input } from '@angular/core';
@Component({
selector:'app-loading-indicator',
template:'<div role="status" aria-live="polite"><span aria-hidden="true"></span>{{label()}}</div>',
styles:[':host{display:block}div{position:fixed;left:50%;top:max(.75rem,env(safe-area-inset-top));transform:translateX(-50%);z-index:50;display:flex;align-items:center;gap:.65rem;padding:.7rem 1rem;border:1px solid var(--line);border-radius:2rem;background:var(--surface);color:var(--text);box-shadow:var(--shadow);font-size:.8rem;max-width:calc(100% - 2rem);pointer-events:none}span{width:18px;height:18px;flex-shrink:0;border:2px solid var(--line);border-top-color:var(--text);border-radius:50%;animation:loading-spin .8s linear infinite}@keyframes loading-spin{to{transform:rotate(360deg)}}@media(prefers-reduced-motion:reduce){span{animation-duration:2s}}']
})
export class LoadingIndicatorComponent {readonly label=input('Working...');}
