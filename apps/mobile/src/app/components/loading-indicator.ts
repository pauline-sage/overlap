import { Component, input } from '@angular/core';
@Component({
  selector: 'app-loading-indicator',
  template: '<div class="loading-message" role="status" aria-live="polite"><span aria-hidden="true"></span>{{ label() }}</div>',
  styles: [`
    :host { position:fixed; inset:0; z-index:1000; display:grid; place-items:center; padding:1rem; background:rgb(0 0 0 / 45%); cursor:wait; }
    .loading-message { display:flex; align-items:center; gap:.75rem; padding:1rem 1.25rem; border:1px solid var(--line); border-radius:1.25rem; background:var(--surface); color:var(--text); box-shadow:var(--shadow); font-size:.9rem; max-width:100%; }
    span { width:24px; height:24px; flex-shrink:0; border:3px solid var(--line); border-top-color:var(--text); border-radius:50%; animation:loading-spin .8s linear infinite; }
    @keyframes loading-spin { to { transform:rotate(360deg); } }
    @media(prefers-reduced-motion:reduce) { span { animation-duration:2s; } }
  `],
})
export class LoadingIndicatorComponent { readonly label = input('Working...'); }
