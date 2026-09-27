import { EventStatus } from './api.models';

// Same rule as the backend (StatusRules). The backend stays authoritative;
// this only decides which buttons the event detail page offers.
export const ALLOWED_TRANSITIONS: Record<EventStatus, EventStatus[]> = {
  OPEN: ['ACKNOWLEDGED', 'RESOLVED'],
  ACKNOWLEDGED: ['RESOLVED'],
  RESOLVED: ['OPEN'],
};

export const TRANSITION_LABELS: Record<EventStatus, string> = {
  OPEN: 'Reopen',
  ACKNOWLEDGED: 'Acknowledge',
  RESOLVED: 'Resolve',
};
