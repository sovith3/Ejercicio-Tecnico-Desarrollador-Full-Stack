# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------------------------
# InvoiceHub API. El contexto de construccion es la raiz del repositorio porque el proyecto de la
# API incluye como contenido los scripts SQL (backend/database/scripts) y el origen del enunciado
# (bd_exam_invoices.json), que vive en la raiz.
# ---------------------------------------------------------------------------------------------

# ---------------------------------------------------------------------------------------------
# Etapa 1: restauracion y publicacion
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Los manifiestos viajan primero: mientras las dependencias no cambien, la restauracion se memoriza
# y la imagen se reconstruye en segundos.
COPY backend/InvoiceHub.sln ./backend/
COPY backend/src/InvoiceHub.Domain/InvoiceHub.Domain.csproj ./backend/src/InvoiceHub.Domain/
COPY backend/src/InvoiceHub.Application/InvoiceHub.Application.csproj ./backend/src/InvoiceHub.Application/
COPY backend/src/InvoiceHub.Infrastructure/InvoiceHub.Infrastructure.csproj ./backend/src/InvoiceHub.Infrastructure/
COPY backend/src/InvoiceHub.Api/InvoiceHub.Api.csproj ./backend/src/InvoiceHub.Api/
RUN dotnet restore backend/src/InvoiceHub.Api/InvoiceHub.Api.csproj

# Codigo, scripts SQL y origen JSON. Solo viaja lo que la aplicacion necesita: los proyectos de pruebas
# no entran en la imagen de produccion. La publicacion deja el arbol listo para ejecutar:
#   /app/database/scripts/*.sql   (fuente de verdad del comportamiento SQL)
#   /app/data/bd_exam_invoices.json (origen importado en el arranque)
COPY bd_exam_invoices.json ./bd_exam_invoices.json
COPY backend/database/ ./backend/database/
COPY backend/src/ ./backend/src/
RUN dotnet publish backend/src/InvoiceHub.Api/InvoiceHub.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false \
    --nologo

# ---------------------------------------------------------------------------------------------
# Etapa 2: ejecucion
# ---------------------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# tzdata: el reloj de negocio resuelve "America/Santiago" desde /usr/share/zoneinfo.
# curl: sonda de salud del contenedor. data/: carpeta escribible para el archivo SQLite.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/data

# El puerto de escucha se declara una sola vez, por ASPNETCORE_URLS: asi el orquestador puede cambiarlo
# (ASPNETCORE_URLS en docker-compose.yml, alimentado por API_INTERNAL_PORT del .env) sin tocar la imagen.
# appsettings.json ya no fija "Urls" para que este valor sea la unica fuente.
ENV TZ=America/Santiago \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

COPY --from=build /app/publish .

# El archivo SQLite vive en /app/data: se monta como volumen para que la base sobreviva al contenedor.
VOLUME ["/app/data"]
EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=5s --start-period=40s --retries=5 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "InvoiceHub.Api.dll"]
