import {TestBed} from '@angular/core/testing';
import {HttpClient} from '@angular/common/http';
import {of} from 'rxjs';
import {AuthService} from '../../../../auth.service';
import {PhoneRemindersComponent} from './phone-reminders';

describe('Phone reminders',()=>{
  const endpoint='https://fcm.googleapis.com/send/device';
  const subscription={endpoint,toJSON:()=>({endpoint,keys:{p256dh:'key',auth:'auth'}}),unsubscribe:vi.fn().mockResolvedValue(true)};
  const manager={getSubscription:vi.fn(),subscribe:vi.fn()};
  const registration={pushManager:manager};
  const auth={submit:vi.fn()};const http={get:vi.fn()};
  const windowDescriptors=new Map<string,PropertyDescriptor|undefined>();
  beforeEach(()=>{
    vi.clearAllMocks();manager.getSubscription.mockResolvedValue(null);manager.subscribe.mockResolvedValue(subscription);auth.submit.mockResolvedValue({});http.get.mockReturnValue(of({available:true,publicKey:'BAAA'}));
    vi.stubGlobal('isSecureContext',true);vi.stubGlobal('PushManager',{});vi.stubGlobal('Notification',{permission:'granted',requestPermission:vi.fn().mockResolvedValue('granted')});
    for(const [name,value] of Object.entries({isSecureContext:true,PushManager:{},Notification})){
      windowDescriptors.set(name,Object.getOwnPropertyDescriptor(window,name));Object.defineProperty(window,name,{configurable:true,writable:true,value});
    }
    vi.stubGlobal('navigator',{serviceWorker:{getRegistration:vi.fn().mockResolvedValue(registration),register:vi.fn().mockResolvedValue(registration),ready:Promise.resolve(registration)}});
    TestBed.configureTestingModule({imports:[PhoneRemindersComponent],providers:[{provide:AuthService,useValue:auth},{provide:HttpClient,useValue:http}]});
  });
  afterEach(()=>{for(const [name,descriptor] of windowDescriptors){if(descriptor)Object.defineProperty(window,name,descriptor);else Reflect.deleteProperty(window,name);}windowDescriptors.clear();vi.unstubAllGlobals();});
  async function render(){const f=TestBed.createComponent(PhoneRemindersComponent);f.detectChanges();await vi.waitFor(()=>expect(f.componentInstance.busy()).toBe(false));f.detectChanges();return f;}
  it('requests permission only on tap, stores a subscription and removes it on disable',async()=>{
    const f=await render();expect(Notification.requestPermission).not.toHaveBeenCalled();
    await f.componentInstance.toggle();expect(auth.submit).toHaveBeenCalledWith('phone-reminders/subscribe',subscription.toJSON());expect(f.componentInstance.enabled()).toBe(true);
    manager.getSubscription.mockResolvedValue(subscription);await f.componentInstance.toggle();
    expect(auth.submit).toHaveBeenCalledWith('phone-reminders/unsubscribe',{endpoint});expect(subscription.unsubscribe).toHaveBeenCalled();expect(f.componentInstance.enabled()).toBe(false);f.destroy();
  });
  it('keeps reminders off when permission is refused',async()=>{
    vi.mocked(Notification.requestPermission).mockResolvedValue('denied');const f=await render();await f.componentInstance.toggle();
    expect(auth.submit).not.toHaveBeenCalled();expect(f.componentInstance.enabled()).toBe(false);expect(f.componentInstance.error()).toContain('Allow notifications');f.destroy();
  });
  it('checks the server owner status instead of treating any browser subscription as enabled',async()=>{
    manager.getSubscription.mockResolvedValue(subscription);auth.submit.mockResolvedValue({enabled:false});const f=await render();
    expect(auth.submit).toHaveBeenCalledWith('phone-reminders/status',{endpoint});expect(f.componentInstance.enabled()).toBe(false);f.destroy();
  });
});
