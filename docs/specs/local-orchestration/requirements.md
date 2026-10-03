# Local orchestration — Requirements

## Introduction

Today running the system means starting PostgreSQL, applying each service's migrations with `dotnet ef database update`, and then starting Auth, Availability, Booking and the gateway in four terminals, in the right order. This spec replaces that with one command: `docker compose up --build` from the repository root builds every service into an image, starts one PostgreSQL container holding the three service databases, applies each service's migrations when it starts, and exposes the whole API on the gateway's port.

It refines the "Docker Compose based local development only" scope line (section 4.1), the "Local Orchestration" row of the technology table (section 15) and the "Docker Compose" item of the MVP must-haves and Week 1 plan (section 16) of [`docs/requirements.md`](../../requirements.md). It has **no FR-ID**: the BRD lists Compose as tooling, not as a functional requirement. It builds on [`api-gateway`](../api-gateway/requirements.md), whose Requirement 1.6 already left the cluster addresses empty so that Compose could supply them as environment variables.

Compose is for local development only. Nothing here is a production deployment, and running the services with `dotnet run` against a local PostgreSQL SHALL keep working exactly as it does today.

## Requirements

### Requirement 1: One command starts the system (no FR-ID)

**User Story:** As a developer, I want to start the whole system with one command on a fresh machine, so that I do not have to start four processes and a database by hand in the right order.

#### Acceptance Criteria

1. The repository root SHALL contain a `docker-compose.yml` defining exactly these services: `postgres`, `auth`, `availability`, `booking` and `gateway`.
2. WHEN a developer with Docker installed has created `.env` (Requirement 4.2) and runs `docker compose up --build` from the repository root, THEN the system SHALL build the four .NET images, start all five containers, and within two minutes of the images being built `GET http://localhost:5290/healthz` SHALL answer `200` with status `Healthy`.
3. The containers SHALL start in dependency order: `auth`, `availability` and `booking` SHALL NOT start until `postgres` reports healthy (a `pg_isready` health check), and `gateway` SHALL NOT start until `auth`, `availability` and `booking` have started. `booking` SHALL NOT depend on `availability` at start-up, because Booking only calls Availability when a request needs it and an outage already surfaces as `503` (booking-appointments).
4. WHEN a developer runs `docker compose down`, THEN every container SHALL stop and be removed; the database contents SHALL survive in a named volume. WHEN they run `docker compose down -v`, THEN the volume SHALL be removed too and the next `up` SHALL start from empty databases.
5. Every .NET container SHALL restart automatically if it exits with an error (`restart: unless-stopped` or `on-failure`), so a service that lost a start-up race recovers without the developer intervening.

### Requirement 2: Container images (no FR-ID)

**User Story:** As a developer, I want each service built into its own small image from the source in the repository, so that Compose runs exactly the code I have checked out.

#### Acceptance Criteria

1. Each of Auth, Availability, Booking and the gateway SHALL have a `Dockerfile` beside its `*.Api` (or gateway) project. The build context SHALL be the repository root, because every service references `src/Shared/`.
2. Each `Dockerfile` SHALL be multi-stage: build and publish with the .NET 10 SDK image, and run on the .NET 10 ASP.NET runtime image. The final image SHALL contain the published output only, not the source or the SDK.
3. Each `Dockerfile` SHALL restore before copying the rest of the source (copying the project files first), so that a code change does not invalidate the restore layer.
4. The repository root SHALL contain a `.dockerignore` that excludes at least `**/bin`, `**/obj`, `.git`, `.vs`, `.env` and `tests/`, so neither build output nor secrets reach the build context.
5. The final image SHALL run as the non-root user the .NET runtime image provides (`app`).
6. Inside its container, each .NET process SHALL listen on plain HTTP on port `8080` (`ASPNETCORE_HTTP_PORTS=8080`). Services SHALL reach each other by Compose service name, for example `http://availability:8080`.

### Requirement 3: Database and migrations (no FR-ID)

**User Story:** As a developer, I want the databases to exist and be up to date when the services start, so that I never have to run `dotnet ef database update` before using the system.

#### Acceptance Criteria

1. `postgres` SHALL run the official `postgres` image (a pinned major version) and store its data in a named volume.
2. WHEN the `postgres` container initialises an empty volume, THEN an init script mounted into `/docker-entrypoint-initdb.d/` SHALL create the databases `smart_appointment_users`, `smart_appointment_availability` and `smart_appointment_booking`, the same names used in local development. Each service SHALL connect only to its own database.
3. Auth, Availability and Booking SHALL each read a boolean setting `Database:MigrateOnStartup`, default `false`. WHEN it is `true`, THEN at start-up, before the application begins serving requests and before Auth's admin seeder runs, the service SHALL apply its pending EF Core migrations to its own database.
4. IF applying migrations fails, THEN the service SHALL log the failure and exit with a non-zero code, rather than serve requests against a schema it could not bring up to date. (Requirement 1.5 then restarts it.)
5. WHEN the database is already up to date, THEN applying migrations SHALL be a no-op, so restarting a container is safe.
6. `appsettings.json` SHALL leave `Database:MigrateOnStartup` off, and so SHALL `appsettings.Development.json`: running a service with `dotnet run` SHALL behave exactly as today. Only `docker-compose.yml` SHALL turn it on.

### Requirement 4: Configuration and secrets (no FR-ID)

**User Story:** As a developer, I want Compose to configure every service from one place without committing secrets, so that the keys that must match across services are set once.

#### Acceptance Criteria

1. Every setting a container needs that differs from `appsettings.json` SHALL be set by `docker-compose.yml` through environment variables in ASP.NET Core's double-underscore form. No `appsettings.*.json` file SHALL be added or changed for Compose, and no image SHALL contain a secret.
2. The secret values (the PostgreSQL password, `Jwt:SecretKey` and `InternalApi:Key`) SHALL come from a `.env` file at the repository root, which SHALL be listed in `.gitignore`. A committed `.env.example` SHALL contain every variable `docker-compose.yml` reads, with throwaway development values that already satisfy the services' start-up checks (both keys at least 32 bytes), so `cp .env.example .env` is the only set-up step.
3. `Jwt__SecretKey` SHALL be set from one `.env` variable for `auth`, `availability`, `booking` and `gateway`, and `InternalApi__Key` from one `.env` variable for `availability` and `booking`, so the values that must match (CLAUDE.md, "Local settings and secrets") cannot drift apart.
4. IF a required variable is missing from `.env`, THEN `docker compose up` SHALL fail before starting any container with a message naming the variable (Compose's `${VAR:?message}` form).
5. Compose SHALL set, for each container: `ConnectionStrings__DefaultConnection` pointing at `postgres` and that service's own database (Auth, Availability, Booking); `Services__Availability__BaseUrl=http://availability:8080` (Booking); and `ReverseProxy__Clusters__{auth,availability,booking}__Destinations__primary__Address` set to `http://auth:8080`, `http://availability:8080` and `http://booking:8080` (gateway). Because the gateway's proxy, its `/healthz` checks and its documentation routes all read those addresses, no other gateway setting SHALL need overriding.
6. The containers SHALL run with `ASPNETCORE_ENVIRONMENT=Development`, so the gateway serves the aggregated Scalar documentation at `http://localhost:5290/scalar` (api-gateway, Requirement 10). The environment variables of criterion 5 SHALL override the `localhost` values in each `appsettings.Development.json`.
7. The optional Auth admin seed (`Seed__Admin__Email`, `__Password`, `__FirstName`, `__LastName`, `__PhoneNumber`) SHALL be passed through from `.env` when present. `.env.example` SHALL list them commented out, and without them the seeder stays inert as it does today.

### Requirement 5: What is reachable from the host (no FR-ID)

**User Story:** As a developer, I want only the gateway and the database reachable from my machine, so that local runs go through the same single entry point as the real system.

#### Acceptance Criteria

1. Compose SHALL publish exactly two ports to the host: the gateway on `5290` (`http://localhost:5290`, the same address as `dotnet run`) and PostgreSQL on `5433`, for database tools and `dotnet ef`. Inside the Compose network PostgreSQL still listens on `5432`; only the host side differs, so it can run alongside a local PostgreSQL on `5432` (the one `dotnet run` uses), and the two never share data.
2. `auth`, `availability` and `booking` SHALL NOT publish any port. In particular `/internal/**` SHALL NOT be reachable from the host.
3. IF port `5290` or `5433` is already in use on the host, THEN the developer SHALL be able to change it in `.env` (`GATEWAY_PORT`, `POSTGRES_PORT`, defaulting to `5290` and `5433`) without editing `docker-compose.yml`.

### Requirement 6: Documentation (no FR-ID)

**User Story:** As a developer new to the repository, I want the README and CLAUDE.md to say how to run the system with Compose, so that I find it without reading the compose file.

#### Acceptance Criteria

1. `README.md` and `CLAUDE.md` SHALL describe the Compose workflow: copy `.env.example` to `.env`, run `docker compose up --build`, open `http://localhost:5290/scalar`, and use `docker compose down -v` to reset the databases.
2. They SHALL state that the Compose PostgreSQL is on host port `5433` and is separate from a local PostgreSQL on `5432`: data created under `dotnet run` is not visible under Compose and vice versa, and `dotnet ef` against the Compose database needs a connection string with `Port=5433`.
3. CLAUDE.md SHALL no longer say there is no `docker-compose.yml`, and SHALL mention `Database:MigrateOnStartup`.

## Out of scope

- **A production deployment.** No registry, TLS, orchestrator or production secrets. The gateway in Compose listens on HTTP only.
- **Hot reload in containers** (`dotnet watch` with mounted source). After a code change, run `docker compose up --build <service>`.
- **RabbitMQ, Redis, Seq, Jaeger** and the services that do not exist yet (Queue, Notification, Reporting). Each gets added to the compose file by the spec that introduces it.
- **.NET Aspire AppHost.** The BRD lists it as optional; it was considered and not chosen.
- **Running the tests in containers.** `dotnet test` stays a host command.
- **Container health checks for the .NET services.** The runtime image has no `curl`, and the gateway's `/healthz` already reports each service (api-gateway, Requirement 8).

## Known gaps

- **Rate limiting by client address behind Docker's port publishing.** The gateway partitions the login limit by `RemoteIpAddress` and deliberately ignores forwarded headers (api-gateway, Requirement 3.5). Behind Docker Desktop's port forwarding, every request from the host may arrive from the same bridge address, so every client on the machine shares one login allowance of 5 per minute. That is harmless for local development but means a local test of "two clients each get 5" will not pass under Compose.
