# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props NuGet.config ./
COPY apps/api/SwarmApp.Api.csproj apps/api/
RUN dotnet restore apps/api/SwarmApp.Api.csproj

COPY apps/api/ apps/api/
RUN dotnet publish apps/api/SwarmApp.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080

# The aspnet image has no HTTP client, and a healthcheck whose command is missing is
# worse than no healthcheck: it fails for a reason unrelated to the application.
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/*

EXPOSE 8080
COPY --from=build /app/publish .
# $APP_UID (1654) is defined by the aspnet image. Running as root inside a container
# is one kernel bug away from the host, and nothing in this app needs it.
USER $APP_UID
# /health/live is the liveness probe: it answers without touching the database, so a
# database outage does not cause the orchestrator to restart a healthy process.
HEALTHCHECK --interval=30s --timeout=3s --start-period=20s --retries=3 \
  CMD curl -fsS http://127.0.0.1:8080/health/live || exit 1
ENTRYPOINT ["dotnet", "SwarmApp.Api.dll"]
