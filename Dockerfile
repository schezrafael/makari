# syntax=docker/dockerfile:1
# Node 24 meets the lockfile's >=24.15.0 requirement; engine-strict checks it.
FROM node:24-bookworm-slim AS web-build
WORKDIR /src/web
COPY src/makari-web/package.json src/makari-web/package-lock.json ./
RUN npm ci --engine-strict
COPY src/makari-web/ ./
RUN npm run build

# Keep this SDK tag aligned with global.json (latestPatch within 10.0.2xx).
FROM mcr.microsoft.com/dotnet/sdk:10.0.203 AS api-build
WORKDIR /src
COPY global.json ./
COPY src/Makari.Api/Makari.Api.csproj src/Makari.Api/
RUN dotnet restore src/Makari.Api/Makari.Api.csproj
COPY src/Makari.Api/ src/Makari.Api/
RUN dotnet publish src/Makari.Api/Makari.Api.csproj -c Release --no-restore -o /out /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080
COPY --from=api-build /out/ ./
COPY --from=web-build /src/web/dist/ ./wwwroot/
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Makari.Api.dll"]
