# One image for the whole site: the API serves the built Angular app from wwwroot, and the same
# image runs the nightly importer when started with a different command (see docker-compose.yml).

FROM node:22-alpine AS web
WORKDIR /web
COPY web/know-ball/package.json web/know-ball/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/know-ball/ ./
COPY docs/prototypes/numbers-dont-lie/ /docs/prototypes/numbers-dont-lie/
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Packages.props ./
COPY src/ src/
RUN dotnet publish src/KnowBall.Api -c Release -o /out/api \
 && dotnet publish src/KnowBall.Importer -c Release -o /out/importer

FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY --from=build /out/api /app/api
COPY --from=build /out/importer /app/importer
COPY --from=web /web/dist/know-ball/browser /app/api/wwwroot

# Retain /data and its legacy photo files for backup and deployment compatibility.
# Behind a reverse proxy, set ForwardedHeaders__KnownNetworks__0 to the network it connects from
# (deploy/docker-compose.yml does), so rate limits see each visitor's own address.
ENV ASPNETCORE_URLS=http://+:8080
# Npgsql loads the Kerberos library when it opens a connection; without it every start logs
# "Cannot load library libgssapi_krb5.so.2". We never use Kerberos, but the noise hides real errors.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
 && rm -rf /var/lib/apt/lists/*
# Run as the image's non-root "app" user. It must own /data before VOLUME, so that a fresh volume
# copies that ownership. Active games no longer upload photos.
RUN mkdir -p /data/photos && chown -R "$APP_UID:$APP_UID" /data
USER $APP_UID
VOLUME /data
EXPOSE 8080

WORKDIR /app/api
ENTRYPOINT ["dotnet", "KnowBall.Api.dll"]
