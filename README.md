# InvoiceHub

Backend y panel de facturación sobre **.NET 8 + SQLite + Redis** para el examen de facturas, notas de
crédito y pagos. La aplicación importa el archivo `bd_exam_invoices.json`, deriva los estados calculados
(estado de factura, estado de cobro y consistencia de montos) dentro del motor de base de datos, expone una
API REST documentada y entrega tres reportes agregados más un panel de control.

---

## 1. Arquitectura

El backend sigue una arquitectura de puertos y adaptadores con cuatro proyectos:

| Proyecto | Responsabilidad |
| --- | --- |
| `InvoiceHub.Domain` | Entidades y reglas de negocio puras (`Invoice`, `CreditNote`, `InvoicePayment`, `InvoiceStatusRules`), invariantes y excepciones de dominio. |
| `InvoiceHub.Application` | Casos de uso, contratos públicos de la API, puertos de salida, política *cache-aside*, normalización de criterios y mapeo a DTO. |
| `InvoiceHub.Infrastructure` | Adaptadores: EF Core para escritura, Dapper para lectura, rutinas SQL, cache Redis/memoria, archivo JSON de origen, seguridad (PBKDF2 + JWT) y reloj. |
| `InvoiceHub.Api` | Superficie HTTP: controladores, ProblemDetails uniforme, health checks, Swagger y arranque (esquema + importación inicial). |

Decisiones transversales:

- **Montos en pesos chilenos con hasta cinco decimales en el backend.** La base de datos guarda enteros
  escalados (`TotalAmountUnits`, `SubtotalUnits`, `AmountUnits`, equivalentes a 1/100000 de peso) y la
  conversión se centraliza en `Money` (`ToUnits`/`FromUnits`). Evita errores de redondeo binario en sumas,
  comparaciones y agregaciones; la API devuelve el valor decimal final (`265019` o `0.00001`) y los cálculos
  del dominio y de las rutinas SQL operan con esas cinco posiciones. El panel, en cambio, presenta el monto
  **redondeado a la unidad de peso** —el primer decimal decide el redondeo— con punto de miles (`1.234.568`).
- **Los estados son derivados, nunca declarados.** `Invoice.Status` se calcula desde las notas de crédito,
  `Invoice.PaymentStatus` desde el pago y la fecha de vencimiento, y `IsConsistent` desde la coincidencia
  entre el total declarado y la suma de subtotales. Los valores del origen se conservan aparte
  (`DeclaredStatus`, `DeclaredPaymentStatus`) para evidenciar divergencias.
- **Sistema activo vs. inconsistencias.** Una factura inconsistente permanece almacenada para auditoría
  pero queda fuera de la búsqueda, del catálogo y de los reportes de cartera; solo aparece en el reporte de
  inconsistencias y no admite notas de crédito ni pagos.
- **Contrato de estados por enum.** Listado, detalle, catálogo y respuesta de nota de crédito entregan los
  estados como valores del dominio (`"Issued"`, `"Partial"`, `"Cancelled"`, `"Pending"`, `"Overdue"`,
  `"Paid"`). Las etiquetas en español viajan solo en los campos `label`/`statusLabel` que existen para la
  interfaz.
- **Los scripts SQL son la fuente de verdad.** El esquema, las rutinas de cálculo y las consultas viven en
  `backend/database/scripts`; el inicializador los aplica en el arranque y registra la huella del esquema.

## 2. Requisitos

- .NET SDK 8.0
- Node.js 20+ (solo para el frontend)
- Redis 7 (opcional: sin Redis la API degrada a cache en memoria)
- Docker 24+ con Compose v2 (opcional: forma recomendada de levantar todo sin instalar .NET ni Redis)

## 3. Puesta en marcha

```bash
# API (aplica el esquema e importa el JSON si la base esta vacia)
dotnet run --project backend/src/InvoiceHub.Api          # http://localhost:5145

# Pruebas (los cuatro proyectos)
dotnet test backend/InvoiceHub.sln

# Frontend
cd frontend && npm install && npm run dev                # http://localhost:5173
```

Credenciales de demostración (sembradas en el arranque): usuario `admin`, contraseña `InvoiceHub.2026`.
Se usan en `POST /api/auth/login` para obtener el token que exigen las operaciones de escritura.

> Antes de la primera ejecución, cree el archivo de configuración desde la plantilla versionada:
> `cp .env.example .env`. Docker Compose lo exige (la contraseña de Redis no tiene valor por omisión) y Vite
> lee de ahí las variables `VITE_*` (ver 3.1). Sin `.env`, `dotnet run` funciona igual con los valores de
> `appsettings.json`, pero el panel usaría los valores por omisión de `frontend/src/config.ts`.

`npm run dev` también lee el `.env` de la raíz (`frontend/vite.config.ts` fija `envDir` en la raíz): de ahí
salen `VITE_API_BASE_URL` (`/api`), `VITE_API_PROXY_TARGET` y `VITE_DEV_PORT`. El servidor de desarrollo
reenvía `/api` y `/health` a la API .NET, de modo que el panel usa rutas relativas en desarrollo y en
contenedor. Para que el desarrollo local use el Redis del compose (que exige contraseña) basta inyectar la
misma cadena que el contenedor:

```bash
ConnectionStrings__RedisConnection="localhost:6379,password=InvoiceHub.Redis.2026" \
  dotnet run --project backend/src/InvoiceHub.Api
```

### 3.1 Todo contenerizado (`.env` centralizado + `docker-compose.yml`)

El stack completo — cache, API y panel — se configura con **un único archivo `.env` en la raíz**. Docker
Compose lo lee solo (interpolación de `${...}` y jerarquía de doble guion bajo de .NET) y Vite lo lee
también, porque su `envDir` apunta a la raíz: no hay valores duplicados entre contenedor y desarrollo.

```bash
cp .env.example .env              # plantilla unica de configuracion del stack
docker compose up --build -d      # Redis + API + panel
docker compose ps                 # los tres quedan "healthy"
docker compose logs -f net8-backend
docker compose down               # detiene y conserva los volumenes
docker compose down -v            # arranque limpio (borra base y cache)
```

| Servicio | Imagen | Publicación | Configuración inyectada |
| --- | --- | --- | --- |
| `redis-cache` | `redis:7-alpine` | `127.0.0.1:6379` (solo loopback) | `--requirepass ${REDIS_PASSWORD}` y sonda de salud autenticada (`redis-cli -a`). |
| `net8-backend` | `Dockerfile` multi-etapa | `localhost:5145` → `8080` | `ASPNETCORE_URLS` con `API_INTERNAL_PORT`, `ConnectionStrings__RedisConnection`, `Cache__*`, `Database__*`, `SourceData__*`, `Security__*`, `Cors__*`. |
| `react-frontend` | `frontend/Dockerfile` (Vite → nginx) | `localhost:5173` → `80` | `VITE_*` como `build.args` (se compilan en el bundle) y `API_UPSTREAM` en tiempo de ejecución. |

**Redis con contraseña.** El servidor arranca con `--requirepass ${REDIS_PASSWORD}` y la API recibe la
credencial por la jerarquía estándar de .NET — una sola cadena, sin repetir el valor:

```yaml
ConnectionStrings__RedisConnection: redis-cache:6379,password=${REDIS_PASSWORD},defaultDatabase=0,abortConnect=false
```

`CacheOptions` resuelve la conexión con esta prioridad: `ConnectionStrings:RedisConnection` >
`Cache:ConnectionString` > `Cache:Host`/`Cache:Port`; los campos `Cache:Password`, `Cache:User`,
`Cache:Database` y `Cache:UseSsl` se aplican siempre sobre la cadena resultante. Si la contraseña fuera
incorrecta, la API **no falla**: el adaptador degrada a memoria y la sonda `/health` lo informa como
`Degraded` con `connected: false` y `passwordConfigured: true` (nunca se registra el valor).

**Rutas y credenciales configurables.** Del `.env` salen también la ruta del archivo SQLite
(`DATABASE_FILE_PATH`), la carpeta de scripts SQL (`SCRIPTS_HOST_PATH` → `/app/database/scripts`), la ruta
del JSON del enunciado (`SOURCE_JSON_HOST_PATH` → `SOURCE_JSON_CONTAINER_PATH`), la zona horaria, la clave
JWT y las credenciales del administrador. El JSON y los scripts viajan como volúmenes de **solo lectura**:
cambiar el origen o una consulta no exige reconstruir la imagen.

**El panel no incrusta la URL de la API en la red.** Vite compila `VITE_API_BASE_URL=/api` (mismo origen) y
nginx resuelve `${API_UPSTREAM}` al arrancar, proxyando `/api`, `/health` y `/swagger` hacia la API: sin CORS
y sin reconstruir el bundle al cambiar de host. Las claves de infraestructura (Redis, JWT) nunca llegan al
navegador: Vite sólo publica las variables con prefijo `VITE_`.

Verificación mínima del entorno contenerizado:

```bash
curl -s localhost:5145/health                 # cache "Healthy (redis)" con connected y passwordConfigured
curl -s localhost:5145/api/diagnostics        # invoiceCount: 50, cache.endpoint redis-cache:6379
curl -s 'localhost:5145/api/invoices?pageSize=5'
curl -s localhost:5173/                       # panel servido por nginx
curl -s localhost:5173/health | head -c 200   # el proxy /health llega a la API (mismo origen)
docker exec -it invoicehub-redis redis-cli -a "$REDIS_PASSWORD" ping   # PONG (sin -a: NOAUTH)
```

> El `.env` **no se versiona** (está en `.gitignore`) y se comparte aparte de este repositorio. Aquí viaja
> `.env.example`, con las mismas claves y los valores de demostración, de modo que el stack se levanta con
> `cp .env.example .env && docker compose up --build -d`. En un despliegue real los secretos viven en el
> gestor de secretos del entorno; Redis, además, sólo se publica en loopback.


## 4. Configuración (`appsettings.json` y variables de entorno)

Cada clave puede sobrescribirse desde el entorno con la jerarquía de doble guion bajo de .NET
(`Seccion__Clave`), que es lo que inyecta `docker-compose.yml` desde el `.env` de la raíz.

| Sección | Clave | Descripción |
| --- | --- | --- |
| `ConnectionStrings` | `RedisConnection` | Cadena completa de Redis (tiene prioridad sobre `Cache:ConnectionString`). |
| `Database` | `FilePath` | Archivo SQLite (relativo al directorio de contenido si no es absoluto). |
| | `ScriptsPath` | Carpeta de scripts SQL; si es nula se resuelve automáticamente. |
| | `RecreateOnSchemaChange` | Recrea la base cuando cambia la huella del esquema. |
| | `SeedAdminUser` | Crea el usuario administrador cuando la tabla está vacía. |
| `SourceData` | `FilePath` | Archivo JSON de origen de facturas. |
| | `ImportOnStartup` | Importa el origen en el arranque solo si la base está vacía. |
| | `MaxFileSizeBytes` | Límite defensivo del tamaño aceptado (64 MB). |
| `Cache` | `Provider`, `UseRedis` | Redis o memoria. Si Redis no responde el adaptador degrada a memoria. |
| | `ConnectionString`, `Host`, `Port` | Endpoint de Redis: la cadena completa gana; si falta, se compone con host y puerto. |
| | `User`, `Password`, `Database`, `UseSsl` | Credenciales y base lógica; se aplican sobre la cadena resultante. |
| | `InstanceName` | Prefijo de las claves cuando se comparte la instancia. |
| | `InvoiceTimeToLiveSeconds`, `ReportTimeToLiveSeconds` | Vigencia de las entradas de lectura (30 s / 60 s). |
| `Clock` | `TimeZoneId` | Zona horaria con la que se resuelve la fecha de negocio (mora y vencimientos). |
| `Security` | `Issuer`, `Audience`, `Key`, `TokenLifetimeMinutes` | Firma y vigencia del JWT. |
| | `RequireAuthentication` | Cuando es `true` toda la API exige token; en `false` solo las escrituras. |
| | `AdminUsername`, `AdminPassword`, `AdminDisplayName`, `AdminRole` | Usuario administrador sembrado en la primera ejecución. |
| `Api` | `UseHttpsRedirection`, `ServeStaticFiles`, `SwaggerEnabled` | Comportamiento del host. |
| `Cors` | `AllowedOrigins` | Orígenes permitidos para el frontend (índices `__0`, `__1`, ... en el entorno). |

El endpoint `GET /api/diagnostics` publica la configuración efectiva sin secretos: `cache.provider`,
`cache.connected`, `cache.passwordConfigured` y `cache.endpoint` (por ejemplo `redis-cache:6379,...`), lo
que permite comprobar desde fuera que la inyección del `.env` llegó al proceso.


## 5. Endpoints

| Método | Ruta | Descripción |
| --- | --- | --- |
| `GET` | `/` | Documento de descubrimiento con la lista de recursos. |
| `GET` | `/health`, `/health/live`, `/health/ready` | Sondas de salud (JSON estable). |
| `GET` | `/api/diagnostics` | Esquema aplicado, cache activa, origen y última importación. |
| `POST` | `/api/auth/login` | Autenticación; devuelve el token JWT. |
| `GET` | `/api/auth/me` | Usuario autenticado (requiere token). |
| `GET` | `/api/invoices` | Búsqueda con filtros, orden y paginación. |
| `GET` | `/api/invoices/{number}` | Detalle con líneas, notas de crédito, pago y estados calculados/declarados. |
| `GET` | `/api/invoices/status-catalog` | Catálogo de estados con cantidad y monto. |
| `POST` | `/api/invoices/{number}/credit-notes` | Emite una nota de crédito (201 + `Location`). |
| `POST` | `/api/invoices/{number}/payments` | Registra el pago único de la factura. |
| `GET` | `/api/imports/source` | Estado del archivo de origen y de la última importación. |
| `POST` | `/api/imports` | Importación/reimportación idempotente. |
| `GET` | `/api/reports/payment-status-summary` | Reporte 2: totales y porcentajes por estado de cobro. |
| `GET` | `/api/reports/overdue-without-activity` | Reporte 1: vencidas sin pago ni nota de crédito. |
| `GET` | `/api/reports/inconsistent-invoices` | Reporte 3: total declarado vs. suma del detalle. |
| `GET` | `/api/dashboard` | Indicadores consolidados del panel. |

Errores: `400` validación de entrada, `401` sin token, `404` recurso inexistente, `409` conflicto
(pago duplicado, importación concurrente), `422` regla de negocio incumplida. Todos usan ProblemDetails e
incluyen `traceId`, `timestampUtc` y `businessCode` cuando corresponde.

## 6. Base de datos

`backend/database/scripts` es la fuente de verdad y se aplica por grupos. La numeración es **contigua del
`01` al `21`** (sin huecos), de modo que el orden alfabético de los archivos coincide con el orden de
aplicación:

| Scripts | Contenido |
| --- | --- |
| `01` – `03` | Tablas, índices y vista `vw_invoice_search` (huella del esquema). |
| `04` – `09` | Rutinas de cálculo: recálculo set-based de estados, alta validada de nota de crédito y de pago, traducción de rechazos y saldo pendiente. |
| `10` – `13` | Carga masiva: staging del lote (JSON → tabla temporal), merge set-based de cliente/factura/detalle/nota de crédito/pago, desenlaces y limpieza del lote. |
| `14` – `21` | Consultas de búsqueda, conteos por estado, reportes y panel. |

La validación de saldo y la unicidad del pago ocurren **dentro de la sentencia SQL** (`INSERT ... SELECT`
con las precondiciones en el `WHERE`), de modo que dos solicitudes concurrentes no puedan dejar el saldo en
negativo ni registrar dos pagos.

La importación aplica el lote completo dentro del motor: los pasos `10` – `13` se ejecutan encadenados en
una única transacción inmediata (`BEGIN IMMEDIATE`), con cinco sentencias set-based (una por tabla) y sin una
ida y vuelta por registro. La aplicación sólo entrega el lote ya validado por el dominio y recibe un
desenlace por factura (`Applied` / `AlreadyExists`), en el mismo orden en que lo envió.

## 7. Pruebas

`dotnet test backend/InvoiceHub.sln` ejecuta **357 pruebas** distribuidas en cuatro proyectos, sin
dependencias externas (no requieren Redis ni una base preexistente):

| Proyecto | Pruebas | Qué cubre |
| --- | --- | --- |
| `InvoiceHub.Domain.Tests` | 70 | Invariantes del agregado, reglas de estado (`InvoiceStatusRules`), notas de crédito (monto, saldo, duplicado, anulada, inconsistente), registro de pago, consistencia de montos, formato chileno de los importes (`MoneyFormat`), `Guard` y usuario. |
| `InvoiceHub.Application.Tests` | 133 | Casos de uso con puertos falsos: búsqueda (normalización, orden, paginación, cache), detalle, catálogo de estados, alta de nota de crédito (incluida la precisión de cinco decimales) y pago (con la traducción de cada desenlace de las rutinas), importación (troceo en lotes, traducción de los desenlaces del motor, registros inválidos que no abortan la carga y lote abortado cuando el motor responde incompleto), login y utilidades (`Money`, claves de cache, huella de criterios). |
| `InvoiceHub.Infrastructure.Tests` | 120 | Componentes reales sobre SQLite temporal: numeración contigua `01` – `21` de los scripts SQL (sin huecos ni archivos huérfanos), esquema y huella, siembra del administrador, rutinas de cálculo (saldo, duplicados, canceladas, inconsistencias, concurrencia), recalculo de estados, carga masiva (grafo completo, idempotencia, duplicados dentro del lote y orden de los desenlaces), búsqueda y ordenamiento sobre la vista, los tres reportes y el panel, lector del JSON, PBKDF2, JWT, cache en memoria y opciones/reloj, incluida la resolución de la conexión de Redis (prioridad de `ConnectionStrings:RedisConnection`, credenciales aplicadas sobre la cadena, descripción sin contraseña e inyección desde el contenedor de dependencias). |
| `InvoiceHub.Api.Tests` | 34 | Integración HTTP con `WebApplicationFactory<Program>`: arranque con importación, descubrimiento, salud, diagnóstico, búsqueda/detalle/catálogo, los tres reportes, panel, login JWT, 401 sin token, 201 + `Location` al crear la nota de crédito (con verificación de la actualización y de la invalidación de cache), 422 por saldo excedido, 409 por pago duplicado, reimportación idempotente y serialización de montos como decimales sin notación científica. |

Cada proyecto de pruebas crea su propia base SQLite en un directorio temporal y la elimina al finalizar, de
modo que las suites son independientes entre sí y pueden ejecutarse en paralelo.

Sin SDK local también se ejecutan dentro de un contenedor, que es la misma base de la imagen de compilación:

```bash
docker run --rm -v "$PWD:/src" -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet test backend/InvoiceHub.sln
```

## 8. Estructura del repositorio

```
bd_exam_invoices.json            Origen de datos del enunciado (50 facturas)
.env.example                     Plantilla de configuracion del stack (el .env real no se versiona)
docker-compose.yml               Redis + API + panel, con volumenes para la base, la cache y los origenes
Dockerfile                       Imagen de la API (multi-etapa; publica scripts SQL y origen)
.gitignore / .dockerignore       Exclusiones de control de versiones y del contexto de construccion
backend/
  database/scripts/              Esquema, rutinas y consultas SQL (fuente de verdad)
  src/InvoiceHub.Domain/         Entidades y reglas de negocio
  src/InvoiceHub.Application/    Casos de uso, contratos, puertos y cache-aside
  src/InvoiceHub.Infrastructure/ EF Core, Dapper, cache, seguridad, reloj, origen JSON
  src/InvoiceHub.Api/            Controladores, ProblemDetails, health checks, Swagger
  tests/                         Cuatro proyectos de pruebas
frontend/                        Panel React + Vite + Tailwind
  Dockerfile                     Imagen del panel (Vite → nginx con proxy inverso a la API)
  nginx/default.conf.template    Proxy de /api, /health y /swagger + respaldo de la SPA
  src/                           Panel: config, cliente HTTP, hooks de React Query, paneles y componentes
```

## 9. Pendientes conocidos

- El panel cubre los flujos del enunciado: panel de control, búsqueda con filtros y paginación, detalle con
  productos, notas de crédito y pago, alta de nota de crédito, registro de pago, los tres reportes y la
  reimportación del origen. Quedan fuera, por no ser parte del alcance evaluado: pruebas de componentes con
  Testing Library, enrutado con URL (`react-router-dom` está instalado, pero la navegación es por pestañas) y
  los gráficos de `recharts` (los indicadores se muestran en tarjetas y tablas).
- Redis se usa solo como cache de lectura (búsquedas 30 s, reportes y panel 60 s) con degradación a memoria;
  no hay pub/sub ni invalidación distribuida entre réplicas.
- El stack no incluye terminación TLS ni un orquestador de secretos: el `.env` con credenciales de
  demostración basta para levantar el entorno, pero en producción se usaría un gestor dedicado.

