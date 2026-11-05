# syntax=docker/dockerfile:1
# Build context is engineering/codebase.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.403 AS build
ARG TARGETARCH
ARG APP_VERSION
ARG VCS_REF
WORKDIR /src
COPY harvest.backend-services/ ./
RUN case "$TARGETARCH" in amd64) rid=linux-x64 ;; arm64) rid=linux-arm64 ;; *) exit 1 ;; esac \
    && dotnet publish service/Harvest.Api/Harvest.Api.csproj -c Release -o /app -r "$rid" \
       --self-contained false -p:SourceRevisionId="$VCS_REF"

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app ./
ENV ASPNETCORE_HTTP_PORTS=8080
USER app
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=4 \
    CMD curl --fail --silent http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "Harvest.Api.dll"]
