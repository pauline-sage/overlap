// Receives notifications only: never caches pages, sessions or API responses.
self.addEventListener('install',event=>event.waitUntil(self.skipWaiting()));
self.addEventListener('activate',event=>event.waitUntil(self.clients.claim()));
self.addEventListener('push',event=>{
  let data;try{data=event.data.json();}catch{return;}
  if(data.kind==='calendar' && /^\d{4}-\d{2}-\d{2}$/.test(data.localDate) && Number.isSafeInteger(data.eventCount) && data.eventCount>0){
    event.waitUntil(self.registration.showNotification("Today's " + (data.eventCount===1 ? 'event' : 'events') + ': ' + data.eventCount,{
      body:'Open Overlap to see your schedule.',tag:'calendar-day-'+data.localDate,renotify:false,
      icon:'/phone-icon.png',data:{kind:'calendar'}
    }));return;
  }
  if(!Number.isSafeInteger(data.contestId) || data.contestId<=0)return;
  event.waitUntil(self.registration.showNotification('Cooking together soon',{
    body:'Your cooking contest starts in a minute or two.',tag:'cooking-contest-'+data.contestId,
    icon:'/phone-icon.png',data:{contestId:data.contestId}
  }));
});
self.addEventListener('notificationclick',event=>{
  event.notification.close();const id=event.notification.data?.contestId;
  const calendar=event.notification.data?.kind==='calendar';
  if(!calendar && (!Number.isSafeInteger(id) || id<=0))return;
  event.waitUntil((async()=>{
    const windows=await self.clients.matchAll({type:'window',includeUncontrolled:true});
    const target=windows.find(client=>new URL(client.url).origin===self.location.origin);
    const path=calendar ? '/?schedule=1' : '/?cookingContest='+id;
    if(target){await target.navigate(path);await target.focus();}
    else await self.clients.openWindow(path);
  })());
});
