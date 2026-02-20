# ── Build stage ──────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

# Copy project files first so NuGet restore is layer-cached independently of source changes
COPY Guessr.slnx .
COPY Guessr/Guessr.csproj Guessr/
COPY Guessr.Tests/Guessr.Tests.csproj Guessr.Tests/

RUN dotnet restore Guessr.slnx

# Copy source code and static assets
COPY Guessr/ Guessr/
COPY Guessr.Tests/ Guessr.Tests/

# Build once in Release so both test and publish share the same artifacts
RUN dotnet build Guessr.slnx \
    --no-restore \
    --configuration Release

# Run tests against the already-built output
RUN dotnet test Guessr.Tests/Guessr.Tests.csproj \
    --no-build \
    --configuration Release

# Publish the web app
RUN dotnet publish Guessr/Guessr.csproj \
    --no-build \
    --configuration Release \
    --output /app/publish

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

LABEL maintainer="guessr-scoreboard"
LABEL description="Guessr Scoreboard - Daily puzzle score tracker"

RUN groupadd -r appuser && useradd -r -g appuser -d /app -s /sbin/nologin appuser

# curl is used by the health check
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /app/publish .

# Create directory for the SQLite database (mount a volume here in k8s)
RUN mkdir -p /data && chown appuser:appuser /data

ARG GIT_SHA=dev
ENV APP_VERSION=$GIT_SHA
ENV DB_PATH=/data/guessr_scores.db

EXPOSE 5000

USER appuser

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:5000/health || exit 1

CMD ["dotnet", "Guessr.dll"]
