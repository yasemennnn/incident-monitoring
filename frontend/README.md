# Incident Monitoring – Angular dashboard

Angular 21 app for the incident monitoring backend: Dashboard, Events (with detail and status change) and Service Status.
Data is loaded over REST; SignalR pushes changes; after a reconnect the pages reload over REST.

## Running with Docker Compose

From the repository root:

```bash
docker compose up -d --build   # builds this app and serves it with nginx on http://localhost:4200
```

## Local development

Requires Node.js 22.12 or newer and the backend running on `http://localhost:8080`.
Stop the Compose frontend first, because it also uses port 4200:

```bash
docker compose stop frontend   # from the repository root
npm install                    # see the note below if this fails
npm start                      # http://localhost:4200
npm test -- --watch=false
npm run build                  # output in dist/frontend
```

The backend only allows the origin `http://localhost:4200`, so keep the default port.
Backend addresses are in `src/environments/environment.ts`.

**npm 10.9 note:** `npm install` can fail with `Cannot read properties of null (reading 'edgesOut')`
(an npm bug with optional peer dependencies). `npx npm@11 install` works.
