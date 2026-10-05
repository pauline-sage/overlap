import { Component, input, output } from '@angular/core';

@Component({
  selector: 'app-book-rating',
  template: `
    <fieldset [disabled]="disabled()">
      <legend>Your rating <span>{{ rating() ? rating() + '/5' : '' }}</span></legend>
      <div class="stars">
        @for (star of stars; track star) {
          <button type="button" [attr.aria-label]="'Rate ' + title() + ' ' + star + (star === 1 ? ' star' : ' stars')"
            [attr.aria-pressed]="rating() === star" [class.filled]="(rating() ?? 0) >= star" (click)="rate.emit(star)">
            <svg viewBox="0 0 24 24" aria-hidden="true"><path d="m12 3 2.8 5.7 6.3.9-4.6 4.5 1.1 6.3L12 17.4l-5.6 3 1.1-6.3L3 9.6l6.2-.9Z" /></svg>
          </button>
        }
      </div>
      @if (rating()) { <button class="clear" type="button" (click)="rate.emit(null)">Clear my rating</button> }
    </fieldset>
    @if (partnerRating()) { <p>Your person: {{ partnerRating() }}/5</p> }
  `,
  styles: `:host{display:block;margin-top:.65rem}fieldset{padding:0;margin:0;border:0;min-width:0}legend,p{font:inherit;font-size:.75rem;color:var(--muted)}legend span{margin-left:.3rem}.stars{display:flex;justify-content:space-between}button{font:inherit;color:var(--muted);border:0;background:none;cursor:pointer;min-height:44px}.stars button{flex:1;min-width:0;display:grid;place-items:center;padding:0}.stars svg{width:23px;height:23px;fill:none;stroke:currentColor;stroke-width:1.4;stroke-linejoin:round}.filled{color:var(--clay)!important}.filled svg{fill:var(--clay-soft)}button:focus-visible{outline:2px solid var(--accent);outline-offset:2px;border-radius:.5rem}fieldset:disabled{opacity:.55}.clear{padding:0;font-size:.7rem}p{margin:.2rem 0}`,
})
export class BookRatingComponent {
  readonly stars = [1,2,3,4,5];
  readonly title = input('this book');
  readonly rating = input<number | null>(null);
  readonly partnerRating = input<number | null>(null);
  readonly disabled = input(false);
  readonly rate = output<number | null>();
}
