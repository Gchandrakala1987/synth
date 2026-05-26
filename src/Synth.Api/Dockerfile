# syntax=docker/dockerfile:1.7

# ---------- 1. Build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy the csproj first to maximize layer caching on restore.
COPY Synth.Api.csproj ./
RUN dotnet restore Synth.Api.csproj

COPY . .
RUN dotnet publish Synth.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---------- 2. Runtime ----------
# No official playwright/dotnet image for .NET 10 yet, so we use the standard ASP.NET base
# and install Chromium's OS dependencies ourselves. Playwright then downloads the browser
# on first startup (controlled by the env var below — set to "true" to skip if you bake
# the browser into a custom base image later).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates curl fonts-liberation libasound2 libatk-bridge2.0-0 \
        libatk1.0-0 libc6 libcairo2 libcups2 libdbus-1-3 libdrm2 libexpat1 \
        libfontconfig1 libgbm1 libglib2.0-0 libgtk-3-0 libnspr4 libnss3 \
        libpango-1.0-0 libx11-6 libx11-xcb1 libxcb1 libxcomposite1 libxdamage1 \
        libxext6 libxfixes3 libxrandr2 libxshmfence1 libxss1 libxtst6 \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_NOLOGO=true \
    DOTNET_RUNNING_IN_CONTAINER=true \
    AGENT_SKIP_BROWSER_INSTALL=false

COPY --from=build /app/publish .

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
  CMD curl -fsS http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Synth.Api.dll"]