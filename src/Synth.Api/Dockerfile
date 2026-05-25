# Multi-stage build. The final image is the official Playwright .NET image because it ships
# with Chromium + every system dependency the browser needs — saving us a long apt-get list.
ARG PLAYWRIGHT_VERSION=v1.47.0

# ---------- 1. Build ----------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Synth.Api.csproj ./
RUN dotnet restore Synth.Api.csproj

COPY . .
RUN dotnet publish Synth.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- 2. Runtime ----------
FROM mcr.microsoft.com/playwright/dotnet:${PLAYWRIGHT_VERSION}-jammy AS runtime
WORKDIR /app

# Skip the in-process `playwright install` step — the base image already has the browsers.
ENV AGENT_SKIP_BROWSER_INSTALL=true \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_NOLOGO=true \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish .

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -fsS http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Synth.Api.dll"]
