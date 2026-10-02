const {readFileSync}=require('node:fs');
const {runInNewContext}=require('node:vm');
const assert=require('node:assert/strict');
const handlers={};const notifications=[];const opened=[];
const self={location:{origin:'https://overlap.test'},skipWaiting:async()=>{},
  addEventListener:(name,handler)=>handlers[name]=handler,
  registration:{showNotification:async(title,options)=>notifications.push({title,...options})},
  clients:{claim:async()=>{},matchAll:async()=>[],openWindow:async(url)=>opened.push(url)}};
runInNewContext(readFileSync(require('node:path').join(__dirname,'../apps/mobile/public/phone-reminders-sw.js'),'utf8'),{self,URL});
(async()=>{
  let pending;const waitUntil=promise=>pending=promise;
  handlers.push({data:{json:()=>({contestId:42})},waitUntil});await pending;
  assert.equal(notifications.length,1);assert.equal(notifications[0].tag,'cooking-contest-42');
  handlers.push({data:{json:()=>({contestId:'../../other'})},waitUntil});assert.equal(notifications.length,1);
  let closed=false;handlers.notificationclick({notification:{data:{contestId:42},close:()=>closed=true},waitUntil});await pending;
  assert.equal(closed,true);assert.deepEqual(opened,['/?cookingContest=42']);
  const existing={url:'https://overlap.test/',navigate:async url=>opened.push(url),focus:async()=>{}};
  self.clients.matchAll=async()=>[existing];handlers.notificationclick({notification:{data:{contestId:43},close:()=>{}},waitUntil});await pending;
  assert.deepEqual(opened,['/?cookingContest=42','/?cookingContest=43']);
  console.log('Phone notification display and links passed.');
})().catch(error=>{console.error(error);process.exitCode=1;});
