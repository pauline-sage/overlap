import { Injectable, inject } from '@angular/core';
import { HttpErrorResponse } from '@angular/common/http';
import { AuthService } from './auth.service';

@Injectable({ providedIn: 'root' })
export class IdleSessionService {
  private readonly auth = inject(AuthService);
  private readonly limit = 30 * 60 * 1000;
  private readonly key = 'overlap-last-activity';
  private lastActivity = 0;
  private lastRenewal = 0;
  private timer?: ReturnType<typeof setInterval>;
  private ended?: () => void;
  private renewing = false;
  private generation = 0;
  private readonly activity = () => {
    if (this.checkExpiry()) return;
    this.lastActivity = Date.now();
    try {
      localStorage.setItem(this.key, String(this.lastActivity));
    } catch {
      /* In-memory fallback. */
    }
  };
  private readonly check = () => {
    void this.tick();
  };
  private readonly storage = (event: StorageEvent) => {
    if (event.key !== this.key) return;
    if (event.newValue === '0') {
      this.finish();
      return;
    }
    const time = Number(event.newValue);
    if (Number.isFinite(time) && time > this.lastActivity && time <= Date.now())
      this.lastActivity = time;
  };
  start(ended: () => void, fresh = false): void {
    this.stop();
    this.ended = ended;
    this.lastActivity = this.lastRenewal = Date.now();
    try {
      const saved = Number(localStorage.getItem(this.key));
      if (!fresh && saved > 0 && saved <= Date.now()) this.lastActivity = saved;
      else localStorage.setItem(this.key, String(this.lastActivity));
    } catch {
      /* Storage is optional. */
    }
    for (const name of ['pointerdown', 'keydown', 'scroll'])
      window.addEventListener(name, this.activity, { passive: true, capture: true });
    window.addEventListener('storage', this.storage);
    document.addEventListener('visibilitychange', this.check);
    this.timer = setInterval(this.check, 1000);
    this.checkExpiry();
  }
  stop(): void {
    this.generation++;
    clearInterval(this.timer);
    this.timer = undefined;
    this.ended = undefined;
    this.renewing = false;
    for (const name of ['pointerdown', 'keydown', 'scroll'])
      window.removeEventListener(name, this.activity, true);
    window.removeEventListener('storage', this.storage);
    document.removeEventListener('visibilitychange', this.check);
  }
  broadcastLogout(): void {
    try {
      localStorage.setItem(this.key, '0');
    } catch {
      /* Optional cross-tab sync. */
    }
  }
  private finish(): void {
    const ended = this.ended;
    this.stop();
    ended?.();
  }
  private checkExpiry(): boolean {
    if (!this.ended) return true;
    if (Date.now() - this.lastActivity < this.limit) return false;
    this.broadcastLogout();
    this.finish();
    // The UI locks immediately, even offline. The server ticket expires independently.
    void this.auth.submit('logout', {}).catch(() => {});
    return true;
  }
  private async tick(): Promise<void> {
    if (this.checkExpiry() || this.renewing || document.hidden) return;
    if (this.lastActivity <= this.lastRenewal || Date.now() - this.lastRenewal < 60_000) return;
    const generation = this.generation;
    this.renewing = true;
    try {
      await this.auth.submit('activity', {});
      if (generation === this.generation) this.lastRenewal = Date.now();
    } catch (error) {
      if (
        generation === this.generation &&
        error instanceof HttpErrorResponse &&
        error.status === 401
      ) {
        this.broadcastLogout();
        this.finish();
      }
    } finally {
      if (generation === this.generation) {
        this.renewing = false;
        this.lastRenewal = Date.now();
      }
    }
  }
}
