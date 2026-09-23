import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  beforeEach(() =>
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }),
  );
  it('fetches CSRF protection before submitting credentials', async () => {
    const http = TestBed.inject(HttpTestingController);
    const pending = TestBed.inject(AuthService).submit('login', {
      email: 'person@example.test',
      password: 'test password',
    });
    http.expectNone('/api/auth/login');
    http.expectOne('/api/auth/csrf').flush(null);
    await Promise.resolve();
    const login = http.expectOne('/api/auth/login');
    expect(login.request.method).toBe('POST');
    login.flush({ email: 'person@example.test' });
    await pending;
    http.verify();
  });
});
