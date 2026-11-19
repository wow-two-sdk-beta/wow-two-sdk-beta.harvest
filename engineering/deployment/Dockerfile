# syntax=docker/dockerfile:1
# Build context is engineering/codebase. The image runs the source host: the engine, its HTTP API, the source readers
# and the console, which it serves from wwwroot on the same origin as /api.

# The console: fe-harvest first, then apps/web, into static files. Plain JavaScript, so it builds on the build machine's
# own platform whatever the image's.
FROM --platform=$BUILDPLATFORM node:24.11.0-bookworm-slim AS console
RUN npm install --global pnpm@10.33.2
WORKDIR /src/harvest.frontend-services
COPY harvest.frontend-services/package.json harvest.frontend-services/pnpm-lock.yaml harvest.frontend-services/pnpm-workspace.yaml ./
COPY harvest.frontend-services/apps/web/package.json ./apps/web/package.json
COPY harvest.frontend-services/packages/fe-harvest/package.json ./packages/fe-harvest/package.json
RUN pnpm install --frozen-lockfile
COPY harvest.frontend-services/ ./
# Declared after the dependency layers, since a changed build argument misses the cache of every RUN after it. Vite
# writes both into the console's harvest-console.json.
ARG APP_VERSION
ARG VCS_REF
RUN pnpm --filter "@harvest/web..." build

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
ARG TARGETARCH
ARG APP_VERSION
ARG VCS_REF
WORKDIR /src
COPY harvest.backend-services/ ./
RUN case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) exit 1 ;; esac \
    && dotnet publish service/HarvestSources.Api/HarvestSources.Api.csproj -c Release -o /app -r "$rid" \
       --self-contained false -p:SourceRevisionId="$VCS_REF"

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# The Data Protection key ring of console sessions lives on the target's `keys` volume (DataProtection:KeysPath); the
# folder exists here, owned by the app user, so a new named volume takes that owner.
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /var/lib/harvest/keys && chown app:app /var/lib/harvest/keys && chmod 700 /var/lib/harvest/keys
WORKDIR /app
COPY --from=build /app ./
# HarvestSources.Api's web root: the host serves these files and falls back to index.html for client routes.
COPY --from=console /src/harvest.frontend-services/apps/web/dist/ ./wwwroot/
ENV ASPNETCORE_HTTP_PORTS=8080
USER app
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=4 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "HarvestSources.Api.dll"]
