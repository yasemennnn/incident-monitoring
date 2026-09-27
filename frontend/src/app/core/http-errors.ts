import { HttpErrorResponse } from '@angular/common/http';

/** Short, user-facing text for a failed API call. The backend returns ProblemDetails bodies. */
export function describeError(error: unknown): string {
  if (!(error instanceof HttpErrorResponse)) {
    return 'Unexpected error.';
  }
  if (error.status === 0) {
    return 'Cannot reach the API. Is the backend running?';
  }
  const problem = error.error as { title?: string; detail?: string; errors?: Record<string, string[]> } | null;
  if (problem?.errors) {
    return Object.values(problem.errors).flat().join(' ');
  }
  return problem?.detail ?? problem?.title ?? `Request failed (HTTP ${error.status}).`;
}

/** 503: a dependency (Redis for dashboard data, or the database) is temporarily down. */
export function isServiceUnavailable(error: unknown): boolean {
  return error instanceof HttpErrorResponse && error.status === 503;
}
