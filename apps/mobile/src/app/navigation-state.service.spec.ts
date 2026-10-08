import { NavigationStateService, oneOf, navigationId } from './navigation-state.service';

describe('Refresh navigation',()=>{
  beforeEach(()=>sessionStorage.removeItem('overlap-navigation'));
  afterEach(()=>{vi.restoreAllMocks();sessionStorage.removeItem('overlap-navigation');});
  it('restores validated navigation in a new app instance and clears it on sign-out',()=>{
    const first=new NavigationStateService();first.write('dashboard.page','notifications');first.write('draw.id',17);
    const restored=new NavigationStateService();
    expect(restored.read('dashboard.page',oneOf(['home','notifications']),'home')).toBe('notifications');
    expect(restored.read('draw.id',navigationId,0)).toBe(17);
    restored.clear();expect(new NavigationStateService().read('draw.id',navigationId,0)).toBe(0);
  });
  it('ignores malformed and invalid saved destinations and handles unavailable storage',()=>{
    sessionStorage.setItem('overlap-navigation','invalid');expect(new NavigationStateService().read('draw.id',navigationId,0)).toBe(0);
    sessionStorage.setItem('overlap-navigation',JSON.stringify({'dashboard.page':'unknown','draw.id':-1}));
    const state=new NavigationStateService();expect(state.read('dashboard.page',oneOf(['home','settings']),'home')).toBe('home');
    expect(state.read('draw.id',navigationId,0)).toBe(0);
    vi.spyOn(Storage.prototype,'setItem').mockImplementation(()=>{throw new Error('Storage disabled');});
    expect(()=>state.write('dashboard.page','settings')).not.toThrow();expect(state.read('dashboard.page',oneOf(['home','settings']),'home')).toBe('settings');
  });
});
