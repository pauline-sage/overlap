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
  <details><summary>View hours</summary>
    <dl>@for(row of rows();track $index){<div><dt>{{row.days}}</dt><dd>{{row.hours}}</dd></div>}</dl>
  </details>`,styles:[`
    :host{display:block;margin:.65rem 0}details{background:var(--accent-soft);border-radius:.75rem}summary{box-sizing:border-box;cursor:pointer;color:var(--text);font-size:.75rem;min-height:44px;align-content:center;padding:.65rem .8rem}summary:focus-visible{outline:2px solid var(--accent);outline-offset:3px;border-radius:.75rem}dl{margin:0;padding:0 .8rem .65rem;font-size:.78rem}dl>div{display:flex;justify-content:space-between;gap:1rem;padding:.35rem 0;line-height:1.5;flex-wrap:wrap}dt{color:var(--muted)}dd{margin:0;text-align:right;font-variant-numeric:tabular-nums;overflow-wrap:anywhere;min-width:0}
  `]})
export class OpeningHoursComponent {
  readonly hours=input.required<string>();
  readonly rows=computed(()=>readableHours(this.hours()));
}
