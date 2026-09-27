import { Component, OnInit, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ConnectionState, RealtimeService } from './core/services/realtime.service';

const CONNECTION_LABELS: Record<ConnectionState, string> = {
  connected: 'Live',
  connecting: 'Connecting…',
  reconnecting: 'Reconnecting…',
  offline: 'Offline, retrying',
};

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.html',
})
export class App implements OnInit {
  protected readonly realtime = inject(RealtimeService);
  protected readonly connectionLabels = CONNECTION_LABELS;

  ngOnInit(): void {
    this.realtime.start();
  }
}
