// Receives notifications only: never caches pages, sessions or API responses.
self.addEventListener('install',event=>event.waitUntil(self.skipWaiting()));
self.addEventListener('activate',event=>event.waitUntil(self.clients.claim()));
self.addEventListener('push',event=>{
  let data;try{data=event.data.json();}catch{return;}
  if(!Number.isSafeInteger(data.contestId) || data.contestId<=0)return;
  event.waitUntil(self.registration.showNotification('Cooking together soon',{
    body:'Your cooking contest starts in a minute or two.',tag:'cooking-contest-'+data.contestId,
    icon:'/phone-icon.png',data:{contestId:data.contestId}
  }));
});
self.addEventListener('notificationclick',event=>{
  event.notification.close();const id=event.notification.data?.contestId;
  if(!Number.isSafeInteger(id) || id<=0)return;
  event.waitUntil((async()=>{
    const windows=await self.clients.matchAll({type:'window',includeUncontrolled:true});
    const target=windows.find(client=>new URL(client.url).origin===self.location.origin);
    if(target){await target.navigate('/?cookingContest='+id);await target.focus();}
    else await self.clients.openWindow('/?cookingContest='+id);
  })());
});
