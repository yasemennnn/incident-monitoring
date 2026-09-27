# Incident Monitoring – Angular dashboard

Angular 21 app for the incident monitoring backend: Dashboard, Events (with detail and status change) and Service Status.
Data is loaded over REST; SignalR pushes changes; after a reconnect the pages reload over REST.

## Requirements

- Node.js 22.12 or newer
- The backend running on `http://localhost:8080` (`docker compose up -d` in the repository root)

The backend only allows the origin `http://localhost:4200`, so keep the default port.

## Commands

```bash
npm install          # see the note below if this fails
npm start            # http://localhost:4200
npm test -- --watch=false
npm run build        # output in dist/frontend
```

Backend addresses are in `src/environments/environment.ts`.

**npm 10.9 note:** `npm install` can fail with `Cannot read properties of null (reading 'edgesOut')`
(an npm bug with optional peer dependencies). `npx npm@11 install` works.
