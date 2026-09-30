import { Component, computed, input } from '@angular/core';
export function readableHours(value:string):{days:string;hours:string}[] {
  if(value.trim()==='24/7')return [{days:'Every day',hours:'Open 24 hours'}];
  const names:Record<string,string>={Mo:'Mon',Tu:'Tue',We:'Wed',Th:'Thu',Fr:'Fri',Sa:'Sat',Su:'Sun'};
  return value.split(';').filter(Boolean).map(part=>{
    const match=part.trim().match(/^((?:Mo|Tu|We|Th|Fr|Sa|Su)(?:[-,](?:Mo|Tu|We|Th|Fr|Sa|Su))*)\s+((?:[0-9]{2}:[0-9]{2}-[0-9]{2}:[0-9]{2})(?:,[0-9]{2}:[0-9]{2}-[0-9]{2}:[0-9]{2})*|off|closed)$/);
    if(!match)return {days:'As listed',hours:part.trim()};
    const days=match[1].replace(/Mo|Tu|We|Th|Fr|Sa|Su/g,d=>names[d]).replaceAll('-',' – ').replaceAll(',',', ');
    const hours=match[2]==='off'||match[2]==='closed'?'Closed':match[2].split(',').map(span=>{
      const [start,end]=span.split('-');return start+' – '+end+(end<start?' (+1 day)':'');
    }).join(' – ');
    return {days,hours};
  });
}
@Component({selector:'app-opening-hours',template:`
  <details><summary>Opening hours <span aria-hidden="true">&#8964;</span></summary>
    <dl>@for(row of rows();track $index){<div><dt>{{row.days}}</dt><dd>{{row.hours}}</dd></div>}</dl>
    <p>Local time at the venue. Confirm before going.</p>
  </details>`,styles:[`
    :host{display:block;margin:.5rem 0}details{border-top:1px solid var(--line)}summary{cursor:pointer;list-style:none;min-height:44px;display:flex;align-items:center;justify-content:space-between;font-size:.8rem}summary::-webkit-details-marker{display:none}dl{margin:0;padding:.65rem .8rem;background:var(--accent-soft);border-radius:.8rem}dl>div{display:flex;justify-content:space-between;gap:1rem;padding:.5rem 0;font-size:.78rem;line-height:1.5}dl>div+div{border-top:1px solid var(--line)}dt{color:var(--muted)}dd{margin:0;text-align:right;font-variant-numeric:tabular-nums;overflow-wrap:anywhere}p{color:var(--muted);font-size:.7rem;line-height:1.5}
  `]})
export class OpeningHoursComponent {
  readonly hours=input.required<string>();
  readonly rows=computed(()=>readableHours(this.hours()));
}
