# Local orchestration — Design

## Overview

The feature is almost entirely files at the repository root and beside each entry project: one `docker-compose.yml`, one `.env.example`, one `.dockerignore`, a PostgreSQL init script and four `Dockerfile`s. The only C# change is an opt-in migration step at start-up in Auth, Availability and Booking (Req 3.3–3.6), because a fresh Compose database has no schema and nobody is going to run `dotnet ef database update` inside a container.

No gateway code changes. The proxy, the `/healthz` downstream checks (`HealthExtensions` reads the first destination of each cluster) and the Development documentation routes (which reuse the `auth`, `availability` and `booking` clusters) all take their addresses from `ReverseProxy:Clusters:*`, so three environment variables re-point all of them (Req 4.5).

Two choices are worth stating up front:

- **The migrator is per service, not shared.** `SmartAppointments.BuildingBlocks.Web` has no EF Core dependency and the gateway references it; adding `Microsoft.EntityFrameworkCore.Relational` there to share fifteen lines would pull EF into the gateway. Each service gets a `DatabaseMigrator` in `Infrastructure/Persistence/`, the same place and shape as Auth's existing `DatabaseSeeder`.
- **Containers run as `Development`** (Req 4.6). That loads each `appsettings.Development.json`, whose `localhost` connection strings and addresses are then overridden by the environment variables Compose sets. Environment variables beat JSON files in the default configuration order, so this needs no code.

## Architecture

| Location | Contents |
|---|---|
| repository root | `docker-compose.yml`, `.env.example`, `.dockerignore`, `.gitignore` gains `.env` |
| `deploy/postgres/init/01-create-databases.sql` | creates the three databases (Req 3.2) |
| `src/Services/{Auth,Availability,Booking}/*.Api/Dockerfile` | multi-stage image per service (Req 2) |
| `src/ApiGateway/SmartAppointments.Gateway/Dockerfile` | gateway image (Req 2) |
| `*.Infrastructure/Persistence/DatabaseMigrator.cs` (×3) | opt-in `Database.MigrateAsync()` (Req 3.3–3.5) |
| `*.Api/Program.cs` (×3) | one call to the migrator after `Build()`, before Auth's seeder (Req 3.3) |
| `README.md`, `CLAUDE.md`, `docs/specs/README.md` | the Compose workflow (Req 6) |

The dependency direction is unchanged: the migrator lives in Infrastructure, next to the `ApplicationDbContext` it migrates, and `Program.cs` in the Api project calls it, exactly as Auth's `Program.cs` calls `DatabaseSeeder.SeedAsync` today.

## Components and interfaces

### `DatabaseMigrator` (one per service)

```csharp
namespace <Service>.Infrastructure.Persistence;

/// <summary>Applies pending EF Core migrations at start-up when Database:MigrateOnStartup is true.</summary>
public static class DatabaseMigrator
{
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    public static bool IsEnabled(IConfiguration configuration) =>
        configuration.GetValue<bool>(MigrateOnStartupKey);          // absent → false (Req 3.3, 3.6)

    public static async Task MigrateIfEnabledAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        // scope → IConfiguration, ILogger, ApplicationDbContext
        // if (!IsEnabled) { log Information "skipped"; return; }
        // try { await context.Database.MigrateAsync(ct); log Information with the applied count }
        // catch (Exception ex) { log Critical "could not apply migrations"; throw; }   (Req 3.4)
    }
}
```

- `MigrateAsync` applies only pending migrations and is a no-op on an up-to-date database (Req 3.5). Before migrating, the migrator reads `GetPendingMigrationsAsync()` so the log line says how many it applied.
- Rethrowing from top-level `Program.cs` ends the process with an unhandled exception, which is a non-zero exit code; Compose's restart policy then retries it (Req 3.4, 1.5). The `Critical` log line comes first so the reason is in `docker compose logs`.
- An invalid value (for example `Database__MigrateOnStartup=yes`) makes `GetValue<bool>` throw at start-up. That is the desired loud failure, not a silent "off".

### `Program.cs`

```csharp
var app = builder.Build();

await DatabaseMigrator.MigrateIfEnabledAsync(app.Services);
await DatabaseSeeder.SeedAsync(app.Services);   // Auth only, unchanged; now runs after the schema exists
```

Availability and Booking get only the first line, in the same position.

### Dockerfiles

All four have the same shape (shown for Booking; the build context is the repository root, Req 2.1):

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Project files first so restore is cached until a csproj changes (Req 2.3).
COPY src/Shared/SmartAppointments.BuildingBlocks/SmartAppointments.BuildingBlocks.csproj src/Shared/SmartAppointments.BuildingBlocks/
COPY src/Shared/SmartAppointments.BuildingBlocks.Web/SmartAppointments.BuildingBlocks.Web.csproj src/Shared/SmartAppointments.BuildingBlocks.Web/
COPY src/Services/Booking/Booking.Domain/Booking.Domain.csproj src/Services/Booking/Booking.Domain/
COPY src/Services/Booking/Booking.Application/Booking.Application.csproj src/Services/Booking/Booking.Application/
COPY src/Services/Booking/Booking.Infrastructure/Booking.Infrastructure.csproj src/Services/Booking/Booking.Infrastructure/
COPY src/Services/Booking/Booking.Api/Booking.Api.csproj src/Services/Booking/Booking.Api/
RUN dotnet restore src/Services/Booking/Booking.Api/Booking.Api.csproj
COPY src/Shared/ src/Shared/
COPY src/Services/Booking/ src/Services/Booking/
RUN dotnet publish src/Services/Booking/Booking.Api/Booking.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Booking.Api.dll"]
```

- Only the service's own folder and `src/Shared/` are copied, so a change in Auth does not rebuild Booking's layers.
- The `aspnet:10.0` image defines the non-root `app` user; `USER app` makes it explicit (Req 2.5).
- The gateway's Dockerfile copies the two shared projects and `src/ApiGateway/SmartAppointments.Gateway/` and publishes `SmartAppointments.Gateway.csproj`.
- Images are tagged by Compose as `smartappointments-<service>`, no registry (out of scope).

### `docker-compose.yml`

```yaml
name: smartappointments

x-dotnet-service: &dotnet-service
  restart: on-failure                       # Req 1.5
  environment: &dotnet-env
    ASPNETCORE_ENVIRONMENT: Development      # Req 4.6
    Jwt__SecretKey: ${JWT_SECRET_KEY:?Set JWT_SECRET_KEY in .env (copy .env.example)}   # Req 4.3, 4.4

services:
  postgres:
    image: postgres:17
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?...}
    ports: ["${POSTGRES_PORT:-5433}:5432"]                       # Req 5.1, 5.3
    volumes:
      - postgres-data:/var/lib/postgresql/data                   # Req 3.1, 1.4
      - ./deploy/postgres/init:/docker-entrypoint-initdb.d:ro    # Req 3.2
    healthcheck:
      test: ["CMD", "pg_isready", "-h", "127.0.0.1", "-U", "postgres"]
      interval: 2s
      timeout: 3s
      retries: 30

  auth:
    <<: *dotnet-service
    build: { context: ., dockerfile: src/Services/Auth/Auth.Api/Dockerfile }
    depends_on: { postgres: { condition: service_healthy } }     # Req 1.3
    environment:
      <<: *dotnet-env
      Database__MigrateOnStartup: "true"
      ConnectionStrings__DefaultConnection: Host=postgres;Port=5432;Database=smart_appointment_users;Username=postgres;Password=${POSTGRES_PASSWORD}
      Seed__Admin__Email: ${SEED_ADMIN_EMAIL:-}                  # Req 4.7 (empty = seeder inert)
      # ... the other four Seed__Admin__* the same way

  availability:   # same, with its database, and InternalApi__Key: ${INTERNAL_API_KEY:?...}
  booking:        # same, plus InternalApi__Key and Services__Availability__BaseUrl: http://availability:8080
                  # depends on postgres only (Req 1.3)

  gateway:
    <<: *dotnet-service
    build: { context: ., dockerfile: src/ApiGateway/SmartAppointments.Gateway/Dockerfile }
    depends_on: [auth, availability, booking]                    # service_started (Req 1.3)
    ports: ["${GATEWAY_PORT:-5290}:8080"]                        # Req 5.1, 5.3
    environment:
      <<: *dotnet-env
      ReverseProxy__Clusters__auth__Destinations__primary__Address: http://auth:8080
      ReverseProxy__Clusters__availability__Destinations__primary__Address: http://availability:8080
      ReverseProxy__Clusters__booking__Destinations__primary__Address: http://booking:8080

volumes:
  postgres-data:
```

- **The `pg_isready` check uses `-h 127.0.0.1` on purpose.** While the official image runs the init scripts, it runs a temporary server that listens on the Unix socket only. A plain `pg_isready` would report healthy during init, and a service could connect before its database exists. Over TCP the check stays unhealthy until the real server starts, which is after the init scripts.
- `auth`, `availability` and `booking` have no `ports:` (Req 5.2). They are reachable only on the Compose network.
- **Empty seed values are safe.** `${SEED_ADMIN_EMAIL:-}` passes an empty string when the variable is unset, and `DatabaseSeeder` already treats blank values as absent (`IsNullOrWhiteSpace`). The seeder therefore stays inert without any Compose conditionals (Req 4.7).
- `ASPNETCORE_HTTP_PORTS` comes from the Dockerfile, so Compose does not repeat it.
- The gateway's `UseHttpsRedirection` finds no HTTPS port in the container. It logs one warning and does not redirect. That is accepted rather than worked around (TLS is out of scope).

### `deploy/postgres/init/01-create-databases.sql`

```sql
CREATE DATABASE smart_appointment_users;
CREATE DATABASE smart_appointment_availability;
CREATE DATABASE smart_appointment_booking;
```

The image runs it once, when the data volume is empty. The `postgres` superuser owns the databases. A role per service would be closer to production, but the local setup uses `postgres` throughout too.

### `.env.example`

```dotenv
# Copy to .env (gitignored). Throwaway local values; never use them anywhere real.
POSTGRES_PASSWORD=password
JWT_SECRET_KEY=<the key from Auth's appsettings.Development.json>
INTERNAL_API_KEY=<the key from Availability's appsettings.Development.json>
# GATEWAY_PORT=5290
# POSTGRES_PORT=5433
# SEED_ADMIN_EMAIL=admin@example.com
# SEED_ADMIN_PASSWORD=...
# SEED_ADMIN_FIRST_NAME=...
# SEED_ADMIN_LAST_NAME=...
# SEED_ADMIN_PHONE_NUMBER=...
```

The two keys reuse the values already committed in the `appsettings.Development.json` files. They are already public and already satisfy the 32-byte checks (Req 4.2), and a token issued by a `dotnet run` Auth is then accepted by a Compose gateway too, which helps when switching between the two.

### `.dockerignore`

```
**/bin/
**/obj/
.git/
.vs/
.vscode/
.claude/
.github/
tests/
docs/
.env
**/*.user
```

Req 2.4. Excluding `docs/` and `tests/` also keeps documentation edits from invalidating the build cache.

## Data model

No change. The migrations that `DatabaseMigrator` applies are the ones already in each `Infrastructure/Persistence/Migrations/` folder.

## Error handling

| Condition | Outcome |
|---|---|
| Required `.env` variable missing | `docker compose up` fails before any container starts, naming the variable (Req 4.4) |
| Port 5290 or 5433 in use on the host | Compose fails to bind and names the port; the developer sets `GATEWAY_PORT` / `POSTGRES_PORT` (Req 5.3) |
| PostgreSQL not ready yet | services wait for `service_healthy` (Req 1.3) |
| Migration fails (lock, bad SQL, unreachable database) | `Critical` log, unhandled exception, non-zero exit, `restart: on-failure` retries (Req 3.4, 1.5) |
| `Database:MigrateOnStartup` not a boolean | start-up throws from `GetValue<bool>`, restart loop visible in logs |
| A service still starting when the gateway is up | gateway `/healthz` reports `Degraded` and names it, until it is ready (api-gateway Req 8) |

## Testing strategy

| What | How | Covers |
|---|---|---|
| `DatabaseMigrator.IsEnabled` | unit tests in each service's test project: absent → false, `"false"` → false, `"true"` → true, built from `ConfigurationBuilder().AddInMemoryCollection` | Req 3.3, 3.6 |
| `MigrateIfEnabledAsync` skips when disabled | unit test: with the flag off, it returns without resolving `ApplicationDbContext` (a service provider that has no DbContext registered, so any resolution would throw) | Req 3.6 |
| Applying migrations | not unit-tested: it is EF Core's `MigrateAsync` against a real PostgreSQL, and the repo has no Testcontainers. Covered by the end-to-end check below | Req 3.3, 3.5 |
| Shipped settings leave it off | test that `appsettings.json` and `appsettings.Development.json` of each service do not set `Database:MigrateOnStartup` to `true` (the files are already linked into test output where needed, or read by path) | Req 3.6 |
| Compose file and `.env.example` agree | `docker compose --env-file .env.example config --quiet` passes in the end-to-end check | Req 4.2, 4.4 |
| End to end | recorded manually in `tasks.md`, like the gateway's: `cp .env.example .env`; `docker compose up --build`; `/healthz` `Healthy`; register, log in and create a branch through `localhost:5290`; `/scalar` loads; `localhost:5008` etc. not reachable; `docker compose restart auth` is a no-op migration; `down` keeps data, `down -v` removes it | Req 1–5 |

The unit tests follow the existing xUnit + Moq style; no integration-test infrastructure is introduced.

## Open questions

None.
