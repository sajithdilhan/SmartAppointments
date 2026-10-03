# Local orchestration — Tasks

> Each task leaves `dotnet build SmartAppointments.slnx` at 0 warnings and `dotnet test SmartAppointments.slnx` green. The tasks are grouped into chunks (A–C) that can be handed to an implementer one at a time; do them in order. The design is final.
>
> Check a box only when the code exists and the tests pass, in the same commit as the code.
>
> All eight tasks are done. Unit tests: Auth 87, Availability 303, Booking 102, Gateway 71, BuildingBlocks 71, all green, 0 build warnings. Chunks A and B and task 7 were implemented in parallel; the three `DatabaseMigrator`s were then made identical apart from their namespace. Availability's Infrastructure project needed an explicit `Microsoft.Extensions.Configuration.Binder` reference for `GetValue<bool>` (Auth and Booking get it transitively).
>
> End to end (Docker Desktop 29, Compose v5, Windows): from `down -v`, `cp .env.example .env` plus the five `SEED_ADMIN_*` values, `docker compose up --build -d` built the four images and started everything in about 106 s on a cold cache; `/healthz` was `Degraded` (auth "unreachable") for a few seconds while Auth migrated and seeded, then `200` `Healthy` with all four entries. The logs showed Auth applying 1 migration and seeding the admin, Availability 4 and Booking 1. Through `localhost:5290`: register `201`, customer and admin login `200`, admin `POST /api/branches` `201` with `Location` `http://localhost:5290/api/Branches/{id}`, customer `POST /api/branches` `403` (the service's), customer `GET /api/branches` `200`, unknown appointment `404` (Booking reachable), `/scalar` `200`, `/openapi/booking/v1.json` `200`, `/internal/slots/{id}` `404`. From the host, `localhost:5008`, `5202`, `5310` and `8080` did not answer; `psql` on `localhost:5433` read the `Users` table. `docker compose restart auth` logged "Applying 0 pending database migration(s)" and "An Admin account already exists". `down` then `up` kept both users; `down -v` then `up` left only the seeded admin.
>
> Observed, not a defect: on an empty database EF Core logs a `fail:` for the `SELECT ... FROM "__EFMigrationsHistory"` it issues before the table exists (both from `GetPendingMigrationsAsync` and inside `MigrateAsync`); the migrations then apply normally. It appears only on the first start of an empty volume. Not exercised: a failing migration and the restart loop it triggers, the login rate limit under Compose (the Known gap in requirements.md), and port overrides through `GATEWAY_PORT`/`POSTGRES_PORT`.

## Chunk A — Opt-in migrations at start-up

- [x] 1. `DatabaseMigrator` in Auth
  - `src/Services/Auth/Auth.Infrastructure/Persistence/DatabaseMigrator.cs`, static, with the shape in the design: `MigrateOnStartupKey`, `IsEnabled(IConfiguration)`, `MigrateIfEnabledAsync(IServiceProvider, CancellationToken)`. It creates a scope, logs `Information` when skipped, logs the pending-migration count, then calls `Database.MigrateAsync`. On failure it logs `Critical` and rethrows.
  - In `Auth.Api/Program.cs`, call `await DatabaseMigrator.MigrateIfEnabledAsync(app.Services);` right after `Build()`, **before** `DatabaseSeeder.SeedAsync`
  - Do not add `Database:MigrateOnStartup` to either appsettings file
  - Tests in `tests/Auth.Tests`: `IsEnabled` is false when absent and when `"false"`, and true when `"true"`. `MigrateIfEnabledAsync` returns without resolving `ApplicationDbContext` when disabled (use a `ServiceCollection` with configuration and logging but no DbContext).
  - _Requirements: 3.3, 3.4, 3.5, 3.6_

- [x] 2. `DatabaseMigrator` in Availability and Booking
  - Same class in `Availability.Infrastructure/Persistence/` and `Booking.Infrastructure/Persistence/`, each against its own `ApplicationDbContext`
  - Call it right after `Build()` in both `Program.cs`
  - Same tests in `tests/Availability.Tests` and `tests/Booking.Tests`
  - _Requirements: 3.3, 3.4, 3.5, 3.6_

- [x] 3. Shipped settings keep migrations off
  - In each of the three service test projects, add a test that reads the Api project's `appsettings.json` and `appsettings.Development.json` and asserts `Database:MigrateOnStartup` is not `true`. Link the files into the test output under a distinct folder, like Gateway.Tests does, so they cannot collide with anything else in the output.
  - _Requirements: 3.6, 4.1_

## Chunk B — Images and Compose

- [x] 4. `.dockerignore`, `.gitignore` and the Postgres init script
  - A root `.dockerignore` with the entries in the design
  - Add `.env` to `.gitignore`, but not `.env.example`
  - `deploy/postgres/init/01-create-databases.sql`, which creates the three databases
  - _Requirements: 2.4, 3.2, 4.2_

- [x] 5. Dockerfiles
  - `Dockerfile` beside `Auth.Api`, `Availability.Api`, `Booking.Api` and `SmartAppointments.Gateway`, multi-stage as in the design. Copy project files and restore first, then copy `src/Shared/` and the service's own folder, then `publish -c Release --no-restore /p:UseAppHost=false`. Run on `aspnet:10.0` with `ASPNETCORE_HTTP_PORTS=8080`, `EXPOSE 8080` and `USER app`.
  - Each builds with `docker build -f <path>/Dockerfile .` from the repository root
  - _Requirements: 2.1, 2.2, 2.3, 2.5, 2.6_

- [x] 6. `docker-compose.yml` and `.env.example`
  - The five services, the `postgres-data` volume, the shared `x-dotnet-service` anchor, the `pg_isready -h 127.0.0.1` health check, the `depends_on` conditions and `restart: on-failure`, all as in the design
  - Required variables use `${VAR:?message}`. The ports use `${GATEWAY_PORT:-5290}` and `${POSTGRES_PORT:-5433}`, and the seed variables `${SEED_ADMIN_*:-}`.
  - Per-service environment as in Req 4.5. `Database__MigrateOnStartup: "true"` is set for auth, availability and booking only.
  - Only `gateway` and `postgres` publish ports
  - `.env.example` holds `POSTGRES_PASSWORD`, plus `JWT_SECRET_KEY` and `INTERNAL_API_KEY` copied from the existing `appsettings.Development.json` values. The ports and the seed variables are commented out.
  - `docker compose --env-file .env.example config --quiet` passes, and `docker compose config` without a `.env` fails, naming a missing variable
  - _Requirements: 1.1, 1.3, 1.4, 1.5, 3.1, 4.2, 4.3, 4.4, 4.5, 4.6, 4.7, 5.1, 5.2, 5.3_

## Chunk C — Documentation and end-to-end check

- [x] 7. Documentation
  - `README.md`: a "Run with Docker Compose" section covering the copy-`.env` step, `up --build`, `http://localhost:5290` and `/scalar`, `down` versus `down -v`, rebuilding one service, and the Compose Postgres on host port `5433` being separate from a local one on `5432`
  - `CLAUDE.md`: drop "There is no `docker-compose.yml`"; add the Compose commands, `Database:MigrateOnStartup`, `.env` and the 5433 note
  - `docs/specs/README.md`: an index row for Local orchestration
  - _Requirements: 6.1, 6.2, 6.3_

- [x] 8. End-to-end check (recorded here, no code)
  - Starting from a clean state (`docker compose down -v`), run `cp .env.example .env` and then `docker compose up --build`
  - `GET http://localhost:5290/healthz` returns `200` `Healthy` within two minutes of the images being built
  - The logs show each service applying its migrations
  - Through `localhost:5290`: register and log in a customer; with a seeded admin, create a branch; `/scalar` loads
  - `localhost:5008`, `5202` and `5310` are not reachable from the host; `psql`/`dotnet ef` reaches Postgres on `5433`
  - `docker compose restart auth` logs zero pending migrations
  - `down` then `up` keeps the users; `down -v` then `up` starts empty
  - Record the results in the note at the top of this file
  - _Requirements: 1.2, 1.4, 3.5, 5.1, 5.2_
