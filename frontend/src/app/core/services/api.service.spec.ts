import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiService } from './api.service';

describe('ApiService', () => {
  let api: ApiService;
  let http: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    api = TestBed.inject(ApiService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('sends only the filters that are set, plus paging', () => {
    api.getEvents({ severity: 'CRITICAL', status: '', source: 'ATS', service: '', search: '  route  ', page: 2, pageSize: 20 })
      .subscribe();

    const request = http.expectOne((r) => r.url === 'http://localhost:8080/api/events');
    expect(request.request.params.keys().sort()).toEqual(['page', 'pageSize', 'search', 'severity', 'source']);
    expect(request.request.params.get('search')).toBe('route');
    expect(request.request.params.get('page')).toBe('2');
    request.flush({ items: [], page: 2, pageSize: 20, totalCount: 0 });
  });

  it('updates the status with a PUT, an encoded event id and the expected status', () => {
    api.updateStatus('EVT 1', 'RESOLVED', 'OPEN').subscribe();

    const request = http.expectOne('http://localhost:8080/api/events/EVT%201/status');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ status: 'RESOLVED', expectedStatus: 'OPEN' });
    request.flush({});
  });
});
