import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { firstValueFrom } from 'rxjs';

export interface Account {
  displayName: string | null;
  email: string;
}
export interface AuthMessage {
  message: string;
}
export interface TogetherStatus {
  id: string;
  hasPhoto: boolean;
  online?: boolean | null;
  invitationExpiresUtc: string | null;
  partner: { id: string; displayName: string | null; hasPhoto: boolean; online?: boolean | null } | null;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  me(): Promise<Account> {
    return firstValueFrom(this.http.get<Account>('/api/auth/me'));
  }
  together(): Promise<TogetherStatus> {
    return firstValueFrom(this.http.get<TogetherStatus>('/api/auth/together/'));
  }
  picks<T>(): Promise<T> {
    return firstValueFrom(this.http.get<T>('/api/auth/watch/picks'));
  }
  books<T>(): Promise<T> {
    return firstValueFrom(this.http.get<T>('/api/auth/books/shelf'));
  }
  async submit<T = AuthMessage>(action: string, body: object): Promise<T> {
    // Refresh antiforgery protection after any change of authenticated identity.
    await firstValueFrom(this.http.get<void>('/api/auth/csrf'));
    return firstValueFrom(this.http.post<T>(`/api/auth/${action}`, body));
  }
}
