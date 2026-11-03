# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY DocumentsApp/src/DocumentsApp.Api/DocumentsApp.Api.csproj DocumentsApp/src/DocumentsApp.Api/
RUN dotnet restore DocumentsApp/src/DocumentsApp.Api/DocumentsApp.Api.csproj

COPY DocumentsApp/src/DocumentsApp.Api/ DocumentsApp/src/DocumentsApp.Api/
RUN dotnet publish DocumentsApp/src/DocumentsApp.Api/DocumentsApp.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# The runtime image switches to the non-root app user. Package installs need root.
USER root

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libreoffice-writer \
        libreoffice-calc \
        libreoffice-impress \
        fonts-noto-cjk \
        fontconfig \
    && rm -rf /var/lib/apt/lists/* \
    && fc-cache -f \
    && test -x /usr/bin/soffice \
    && mkdir -p /data/storage /home/app \
    && chown -R app:app /data /home/app

WORKDIR /app
COPY --from=build --chown=app:app /app/publish .
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN chmod 755 /usr/local/bin/docker-entrypoint.sh

# Container defaults. Local `dotnet run` keeps the Mac paths in appsettings.json.
# Render env vars (or `docker run -e`) override these.
ENV ASPNETCORE_ENVIRONMENT=Production \
    PORT=10000 \
    ConnectionStrings__Default="Data Source=/data/documents.db" \
    Storage__RootPath=/data/storage \
    Preview__LibreOfficePath=/usr/bin/soffice \
    SAL_USE_VCLPLUGIN=svp \
    HOME=/home/app

EXPOSE 10000

USER app
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
