# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------------------------
# InvoiceHub Web (React + Vite + Tailwind). La etapa 1 compila la SPA inyectando las variables VITE_*;
# la etapa 2 la sirve con nginx, que ademas actua de proxy inverso hacia la API .NET (mismo origen, sin
# CORS y sin URL absoluta incrustada).
# ---------------------------------------------------------------------------------------------

# ---------------------------------------------------------------------------------------------
# Etapa 1: compilacion del bundle
# ---------------------------------------------------------------------------------------------
FROM node:22-alpine AS build
WORKDIR /app

# Los manifiestos viajan primero: mientras las dependencias no cambien, la capa queda memorizada.
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY . .

# Vite resuelve import.meta.env en tiempo de COMPILACION: estos argumentos llegan desde el .env raiz
# (docker compose los pasa como build.args) y quedan incrustados en el bundle estatico.
ARG VITE_API_BASE_URL=/api
ARG VITE_API_TIMEOUT_MS=15000
ARG VITE_APP_TITLE=Facturia · Control de facturacion
ARG VITE_ADMIN_USER=admin
ARG VITE_ADMIN_PASSWORD=InvoiceHub.2026
ENV VITE_API_BASE_URL=${VITE_API_BASE_URL} \
    VITE_API_TIMEOUT_MS=${VITE_API_TIMEOUT_MS} \
    VITE_APP_TITLE=${VITE_APP_TITLE} \
    VITE_ADMIN_USER=${VITE_ADMIN_USER} \
    VITE_ADMIN_PASSWORD=${VITE_ADMIN_PASSWORD}

RUN npm run build

# ---------------------------------------------------------------------------------------------
# Etapa 2: servidor estatico y proxy inverso
# ---------------------------------------------------------------------------------------------
FROM nginx:1.27-alpine AS runtime

# El destino del proxy se resuelve en tiempo de EJECUCION: el entrypoint de nginx sustituye
# ${API_UPSTREAM} al arrancar. NGINX_ENVSUBST_FILTER limita la sustitucion a esa variable para no
# tocar las variables propias de nginx ($host, $uri, ...).
ENV API_UPSTREAM=http://net8-backend:8080 \
    NGINX_ENVSUBST_FILTER=API_UPSTREAM

COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html

EXPOSE 80

HEALTHCHECK --interval=15s --timeout=5s --start-period=10s --retries=5 \
    CMD wget -q -O /dev/null http://127.0.0.1/healthz || exit 1

CMD ["nginx", "-g", "daemon off;"]
