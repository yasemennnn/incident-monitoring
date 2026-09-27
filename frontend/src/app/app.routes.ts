import { Routes } from '@angular/router';
import { DashboardPage } from './features/dashboard/dashboard-page';
import { EventDetailPage } from './features/events/event-detail-page';
import { EventsPage } from './features/events/events-page';
import { ServicesPage } from './features/services/services-page';

export const routes: Routes = [
  { path: '', component: DashboardPage, title: 'Dashboard · Incident Monitoring' },
  { path: 'events', component: EventsPage, title: 'Events · Incident Monitoring' },
  { path: 'events/:id', component: EventDetailPage, title: 'Event · Incident Monitoring' },
  { path: 'services', component: ServicesPage, title: 'Service Status · Incident Monitoring' },
  { path: '**', redirectTo: '' },
];
