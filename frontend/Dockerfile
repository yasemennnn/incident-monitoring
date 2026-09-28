# Production build of the Angular app, served as static files by nginx.
# The browser calls the API and the SignalR hub on http://localhost:8080 itself (see src/environments/environment.ts);
# nginx only serves the files.

# Build stage: Node is only needed to build, so it is not part of the final image.
FROM node:22-slim AS build
WORKDIR /app
# No Angular CLI usage-data prompt or config change during the build.
ENV NG_CLI_ANALYTICS=false
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

# Runtime stage: only nginx and the built files.
FROM nginx:alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html
