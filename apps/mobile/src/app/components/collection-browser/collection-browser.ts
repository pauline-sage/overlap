import { Component, input, output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

export class CollectionBrowser {
  readonly query = signal('');
  readonly page = signal(1);
  constructor(readonly size=5) {}

  reset(): void { this.search(''); }
  search(query: string): void { this.query.set(query); this.page.set(1); }
  matches(text: string): boolean { return text.toLocaleLowerCase().includes(this.query().trim().toLocaleLowerCase()); }
  pages(count: number): number { return Math.max(1, Math.ceil(count / this.size)); }
  current(count: number): number { return Math.min(this.page(), this.pages(count)); }
  slice<T>(items: T[]): T[] {
    const start = (this.current(items.length) - 1) * this.size;
    return items.slice(start, start + this.size);
  }
}

@Component({
  selector: 'app-collection-browser', imports: [FormsModule],
  templateUrl: './collection-browser.html', styleUrl: './collection-browser.scss',
})
export class CollectionBrowserComponent {
  readonly searchable=input(true);
  readonly more=input(false);
  readonly pending=input(false);
  readonly nextBatch=output<void>();
  next():void {
    const current=this.browser().current(this.count());
    const pages=this.browser().pages(this.count());
    if(this.more() && (current===pages || (current+1===pages && this.count()%this.browser().size!==0))) this.nextBatch.emit();
    else if(current<pages) this.browser().page.set(current+1);
  }
  readonly browser = input.required<CollectionBrowser>();
  readonly total = input.required<number>();
  readonly count = input.required<number>();
  readonly searchId = input.required<string>();
  readonly searchLabel = input('Search your collection');
}
