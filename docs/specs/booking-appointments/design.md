# Booking appointments — Design

> References like _(Req 3.2)_ point at [`requirements.md`](requirements.md).

## Overview

Five decisions shape the feature. They, and the later choices in *Decisions taken* at the end, were taken by the project owner and are recorded here, not reopened.

1. **Scope.** Reserve and release in Availability (`FR-AVL-005`); in a new Booking service, create (with `Idempotency-Key`), get by id and cancel (`FR-BKG-001/002/005`). Not built here: my-appointments, the staff branch view, complete and no-show, the outbox, RabbitMQ and events (**deferred to the Week 2 messaging work**), gateway routing, Docker.
2. **Reservations are rows, not just a counter.** Availability gains a `SlotReservation` table keyed by `(SlotId, AppointmentId)`. The counter `Slot.ReservedCount` stays the fast thing to query, but the row is what makes reserve and release idempotent: a repeat reserve finds its row and does nothing, a release without a row does nothing. HTTP retries therefore can never double-count or double-release _(Req 3.3, 4.2)_.
3. **The slot is claimed with one atomic conditional `UPDATE`.** `UPDATE "Slots" SET "ReservedCount" = "ReservedCount" + 1 WHERE "Id" = @id AND "ReservedCount" < "Capacity"` through `ExecuteUpdateAsync`, in the same transaction as the reservation insert. No read-then-write, so no lost update and no oversell; a check constraint backs it up _(Req 3.2, 3.7)_.
4. **Internal calls are authenticated by a shared API key and nothing else.** A new `AddApiKeyAuthentication(configuration)` in `BuildingBlocks.Web` registers the scheme `Constants.ApiKeyAuthenticationScheme` and the policy `Constants.InternalServicePolicy`, which accepts only that scheme. A JWT cannot reach `/internal`, and the API key cannot satisfy a role policy _(Req 1)_. The constants `ApiKeyAuthenticationScheme` and `ApiKeyHeaderName` have existed unused since the start; this uses them.
5. **Booking generates the appointment id first, and the idempotency record carries it.** The id is a version 7 GUID created when the key is claimed, stored on the idempotency record, and passed to Availability as the reservation key. A retry after a crash reuses the id from the record, so a reservation Availability already made is found instead of leaked _(Req 6.8)_.

The create flow is a small saga without a distributed transaction: claim the key, read the slot, check overlap, reserve (remote), save the appointment and the idempotency outcome in one local transaction, with a compensating release if that save fails. Release is idempotent, which is what makes the compensation and Polly's retries safe.

Everything else follows the existing services: MediatR handlers returning `Result<T>`, FluentValidation, `TimeProvider` for "now", controllers that only dispatch.

## Architecture

| Layer | Contents |
|---|---|
| `SmartAppointments.BuildingBlocks` | `Constants.InternalServicePolicy` (new); `Constants.ApiKeyAuthenticationScheme` and `ApiKeyHeaderName` (existing) |
| `SmartAppointments.BuildingBlocks.Web` | `Authentication/ApiKeyAuthenticationHandler`, `ApiKeyAuthenticationOptions`, `ApiKeyAuthenticationExtensions.AddApiKeyAuthentication`; `Results/ErrorResultExtensions` learns `422` and `503` |
| `Availability.Api` | `Controllers/InternalSlotsController` (`/internal/slots`); `Program.cs` calls `AddApiKeyAuthentication` |
| `Availability.Application` | `GetInternalSlotQuery`, `ReserveSlotCommand`, `ReleaseSlotCommand`, their handlers and validators; `ISlotReservationRepository`; `InternalSlotResponse`, `SlotReservationRequest`; errors in `SlotErrors` |
| `Availability.Domain` | `SlotReservation` entity; `Slot` comment fixed |
| `Availability.Infrastructure` | `SlotReservations` table, `SlotReservationRepository` (the transactional SQL), migration `AddSlotReservations` |
| `Booking.Api` | `Controllers/AppointmentsController`; `Program.cs`; `appsettings*.json` |
| `Booking.Application` | `CreateAppointmentCommand`, `GetAppointmentQuery`, `CancelAppointmentCommand` and handlers; validators; `IAppointmentRepository`, `IIdempotencyRepository`, `IAvailabilityClient`; `AppointmentResponse`, `CreateAppointmentRequest`, `SlotInfo` |
| `Booking.Domain` | `Appointment`, `AppointmentStatus`, `IdempotencyRecord` |
| `Booking.Infrastructure` | `ApplicationDbContext`, `AppointmentRepository`, `IdempotencyRepository`, `AvailabilityClient` and the resilient `HttpClient` registration, `DesignTimeDbContextFactory`, migration `InitialCreate` |
| `tests/Booking.Tests` | xUnit + Moq, like the other test projects |

## Components and interfaces

### Shared: API-key authentication _(Req 1)_

```csharp
public static IServiceCollection AddApiKeyAuthentication(this IServiceCollection services, IConfiguration configuration)
```

- Reads `InternalApi:Key`; throws `InvalidOperationException` with an explanatory message if it is missing, blank, or under 32 bytes (the same minimum as the JWT key) _(Req 1.6)_.
- Registers `AddAuthentication().AddScheme<ApiKeyAuthenticationOptions, ApiKeyAuthenticationHandler>(Constants.ApiKeyAuthenticationScheme, ...)`. It **does not** touch the default schemes, which stay JWT bearer, so every existing `[Authorize]` keeps meaning what it meant.
- Registers `InternalServicePolicy` with `AddAuthenticationSchemes(ApiKeyAuthenticationScheme)` and `RequireAuthenticatedUser()`. Because the policy names only the API-key scheme, a bearer token is never examined for `/internal` and gets `401` _(Req 1.3)_. Because the role policies run on the default (JWT) scheme, a request with only an API key is unauthenticated for them and gets `401` _(Req 1.4)_.

`ApiKeyAuthenticationHandler`:

1. Header `X-API-Key` absent → `AuthenticateResult.NoResult()` (the challenge answers `401`) _(Req 1.2)_.
2. Otherwise compare in constant time. Both the presented value and the configured key are hashed with SHA-256, and the 32-byte digests compared with `CryptographicOperations.FixedTimeEquals`. Hashing first gives equal-length inputs, so neither the content nor the length of the key leaks through timing _(Req 1.5)_.
3. Match → a `ClaimsPrincipal` with a single identity named `internal-service` and **no role claims**, so it can satisfy no role policy even if mis-attached. Mismatch → `AuthenticateResult.Fail("Invalid API key.")`; the message is never the key _(Req 1.7)_.

`ErrorResultExtensions.ToActionResult` currently sends every status other than 400, 401, 403, 404 and 409 to `500`. It gains `422` → `UnprocessableEntity(error)` and `503` → `StatusCode(503, error)` _(Req 10.6)_. This is a change to a shared file, tested in `ErrorResultExtensionsTests`.

### Availability: endpoints

| Verb / route | Action | Authorization | Dispatches | Req |
|---|---|---|---|---|
| `GET /internal/slots/{id:guid}` | `InternalSlotsController.Get` | `InternalServicePolicy` | `GetInternalSlotQuery` | 2 |
| `POST /internal/slots/{id:guid}/reserve` | `InternalSlotsController.Reserve` | `InternalServicePolicy` | `ReserveSlotCommand` | 3 |
| `POST /internal/slots/{id:guid}/release` | `InternalSlotsController.Release` | `InternalServicePolicy` | `ReleaseSlotCommand` | 4 |

`InternalSlotsController` is `[Route("internal/slots")]`, `[ApiController]`, with `[Authorize(Policy = Constants.InternalServicePolicy)]` on the class. It sits beside `SlotsController` rather than inside it so that the public and internal surfaces cannot share an attribute by accident, and so a gateway rule can block `/internal/**` at the edge later. Success is `Ok(InternalSlotResponse)` for the read and `NoContent()` for the writes; failures go through `this.ToActionResult`. `Program.cs` adds `builder.Services.AddApiKeyAuthentication(builder.Configuration)` after `AddJwtAuthentication`.

### Availability: commands, queries and handlers

| Request | Handler | Returns |
|---|---|---|
| `GetInternalSlotQuery(Guid Id)` | `ISlotRepository.GetByIdAsync` (no tracking) → map | `Result<InternalSlotResponse>` |
| `ReserveSlotCommand(Guid SlotId, Guid AppointmentId)` | see below | `Result<bool>` (value unused; `true`) |
| `ReleaseSlotCommand(Guid SlotId, Guid AppointmentId)` | validate → slot exists? → `ReleaseAsync` | `Result<bool>` |

`ReserveSlotCommandHandler`:

1. Validate (Req 3.6) → 400.
2. `ISlotRepository.GetByIdAsync` → missing → 404 (Req 3.5).
3. `ISlotReservationRepository.ExistsAsync(slotId, appointmentId)` → true → success. This runs **before** the state checks (Req 3.3).
4. Load the branch and the service type (`IBranchRepository.GetByIdAsync`, `IServiceTypeRepository.GetByIdAsync`; both exist). State checks in this order, each a `409` with its own message (Req 3.4): `slot.StartUtc <= timeProvider.GetUtcNow().UtcDateTime` → "Slot has already started."; branch inactive → "Branch '{Code}' is inactive."; service type inactive → "Service type '{Code}' is inactive.".
5. `ISlotReservationRepository.ReserveAsync(slotId, appointmentId, now)`: `Reserved` or `AlreadyReserved` → success; `Full` → `409` "Slot is full.".

Steps 3 and 5 both look at the reservation because a concurrent duplicate can slip between them; step 5 is the authority, step 3 is only the early exit that skips the state checks.

`ReleaseSlotCommandHandler` has no state checks (Req 4.3): validate, slot exists (404), `ReleaseAsync`, success whichever outcome comes back.

### Availability: abstractions

```csharp
public enum ReserveOutcome { Reserved, AlreadyReserved, Full }
public enum ReleaseOutcome { Released, NotReserved }

public interface ISlotReservationRepository
{
    Task<bool> ExistsAsync(Guid slotId, Guid appointmentId, CancellationToken ct);                         // no tracking
    Task<ReserveOutcome> ReserveAsync(Guid slotId, Guid appointmentId, DateTime nowUtc, CancellationToken ct);  // owns its transaction
    Task<ReleaseOutcome> ReleaseAsync(Guid slotId, Guid appointmentId, CancellationToken ct);              // owns its transaction
}
```

`ISlotRepository` gains `Task<Slot?> GetByIdAsync(Guid id, CancellationToken ct)` (no tracking).

The two write methods own their transactions, unlike the stage-then-`SaveChangesAsync` pattern of the other repositories, because the correctness argument is one transaction containing one conditional statement, and splitting it across the abstraction would let a caller get it wrong.

`SlotReservationRepository.ReserveAsync`, in one `context.Database.BeginTransactionAsync` (Read Committed, the PostgreSQL default; `NpgsqlRetryingExecutionStrategy` is not enabled, so a manual transaction is allowed):

```sql
INSERT INTO "SlotReservations" ("SlotId", "AppointmentId", "CreatedAtUtc")
VALUES (@slot, @appt, @now)
ON CONFLICT ("SlotId", "AppointmentId") DO NOTHING;           -- affected rows: 1 or 0
```

- 0 rows → the reservation exists → commit, return `AlreadyReserved`. (If a concurrent identical request is mid-transaction, `ON CONFLICT DO NOTHING` waits for it to commit or roll back, then decides: no double insert is possible.)
- 1 row → `context.Slots.Where(s => s.Id == slotId && s.ReservedCount < s.Capacity).ExecuteUpdateAsync(s => s.SetProperty(x => x.ReservedCount, x => x.ReservedCount + 1))`.
  - 1 row affected → commit, `Reserved`.
  - 0 rows → the slot is full; **roll back**, which removes the reservation row inserted a moment ago, return `Full`.

The insert goes first so that the unique key, not application logic, decides duplicates; the conditional update second so that a full slot rolls the row back in the same breath. Two requests racing for the last place serialise on the row lock the `UPDATE` takes: the second re-evaluates `ReservedCount < Capacity` after the first commits, matches 0 rows, and gets `Full` _(Req 3.2)_.

`ReleaseAsync`, in one transaction: `context.SlotReservations.Where(r => r.SlotId == slotId && r.AppointmentId == appointmentId).ExecuteDeleteAsync()`. 0 rows → commit, `NotReserved`. 1 row → `Slots.Where(s => s.Id == slotId && s.ReservedCount > 0).ExecuteUpdateAsync(ReservedCount - 1)`, commit, `Released`. Two simultaneous releases of one reservation: the second `DELETE` waits on the row lock, then finds nothing, so only one decrement happens _(Req 4.1, 4.2)_.

`ExecuteUpdateAsync` and `ExecuteDeleteAsync` bypass the change tracker, so these repositories never load a `Slot` they then write; the entity's `ReservedCount` setter stays private.

### Availability: validation

| Validator | Rules |
|---|---|
| `ReserveSlotCommandValidator` | `SlotId` and `AppointmentId` not empty _(Req 3.6)_ |
| `ReleaseSlotCommandValidator` | the same _(Req 4.4)_ |

### Booking: endpoints

| Verb / route | Action | Authorization | Dispatches | Req |
|---|---|---|---|---|
| `POST /api/appointments` | `AppointmentsController.Create` | `CustomerPolicy` | `CreateAppointmentCommand` | 5, 6 |
| `GET /api/appointments/{id:guid}` | `AppointmentsController.GetById` | `AllowedOriginsPolicy` | `GetAppointmentQuery` | 7 |
| `POST /api/appointments/{id:guid}/cancel` | `AppointmentsController.Cancel` | `AllowedOriginsPolicy` | `CancelAppointmentCommand` | 8 |

`AllowedOriginsPolicy` is the existing name for "any of the three roles"; for get and cancel, the handler then decides what each role may do, because ownership is data the policy cannot see.

Controller details:

- The caller's id is `Guid.Parse` of the `sub` claim (`Constants.UserIdClaimType`; `MapInboundClaims = false` keeps the name), and the role is the `role` claim. A missing or unparseable `sub` returns `401`.
- `Create` reads `Idempotency-Key` with `[FromHeader(Name = "Idempotency-Key")]`. Missing or blank is a `400` from the validator (not the model binder), so the message is ours.
- A successful create returns `CreatedAtAction(nameof(GetById), new { id }, appointment)`. A replayed `201` is returned the same way; the id comes from the stored body, so the `Location` is identical (Req 6.3). A failure, replayed or fresh, goes through `this.ToActionResult(error)` (which now knows `422` and `503`).
- `Cancel` returns `NoContent()`.
- `AddControllers().AddJsonOptions(JsonStringEnumConverter)`, as in Availability, so `Status` is `"Booked"`.

### Booking: commands, queries and handlers

| Request | Returns |
|---|---|
| `CreateAppointmentCommand(Guid CustomerId, string IdempotencyKey, Guid SlotId)` | `Result<AppointmentResponse>` |
| `GetAppointmentQuery(Guid Id, Guid CallerId, string CallerRole)` | `Result<AppointmentResponse>` |
| `CancelAppointmentCommand(Guid Id, Guid CallerId, string CallerRole)` | `Result<bool>` |

#### `CreateAppointmentCommandHandler`

Dependencies: `IIdempotencyRepository`, `IAppointmentRepository`, `IAvailabilityClient`, `IValidator<CreateAppointmentCommand>`, `TimeProvider`, `ILogger`.

1. **Validate** (Req 5.3, 6.1) → `400`. Nothing is recorded.
2. **Hash** the request: SHA-256 of the lower-case `slotId` in `"D"` form, hex-encoded. Only fields the client controls are hashed, and the key and the user are the record's identity, not part of the hash.
3. **Claim the key** — `IIdempotencyRepository.ClaimAsync(userId, key, hash, newAppointmentId, now)` returns one of:
   - `Claimed(record)`: this request owns the key; proceed.
   - `Replay(statusCode, body)`: a completed, unexpired record with an equal hash → rebuild and return it: a stored `201` body deserialises to `Result.Success(AppointmentResponse)`, a stored `4xx` body to `Result.Failure(Error)` _(Req 6.3)_.
   - `HashMismatch` → `422` "This Idempotency-Key was already used with a different request." _(Req 6.4)_.
   - `InProgress` → `409` "A request with this Idempotency-Key is still being processed." _(Req 6.5)_.
4. **Read the slot**: `IAvailabilityClient.GetSlotAsync`. `404` → outcome `404` "Slot not found."; `503` → go to *failure handling* below.
5. **Overlap pre-check**: `IAppointmentRepository.HasOverlappingBookedAsync(customerId, slot.StartUtc, slot.EndUtc)` → `409` "You already have an appointment at that time." _(Req 5.5)_.
6. **Reserve**: `IAvailabilityClient.ReserveAsync(slotId, appointmentId)`. `409` → outcome `409` with Availability's reason _(Req 5.6)_; `404` → outcome `404`; `503` → failure handling.
7. **Save**: build `Appointment.Book(...)` and the `AppointmentResponse`; serialise it; `IAppointmentRepository.TryAddBookedAsync(appointment, record.Complete(201, body, now))` saves both in one transaction (see *the overlap race* below). `Overlap` → release the reservation (`ReleaseAsync`); if the release succeeds, outcome `409` as in step 5 _(Req 5.9)_; if it fails, treat as *failure handling* below, because a reservation is now held without an appointment. An exception → release as compensation (best effort; failure is logged), then rethrow so `ExceptionMiddleware` returns `500`; the claim is **left in progress** _(Req 5.7, 6.7)_.
8. Return `Result.Success(response)` → controller `201`.

*Outcome handling.* For a `201` the record was completed inside step 7. For a business `4xx` (steps 4-6, plus the overlap), the handler calls `IIdempotencyRepository.CompleteAsync(record, status, bodyJson)` where the body is the serialised `Error`, then returns the failure _(Req 6.6)_.

*Failure handling (503 or an unexpected exception).* The handler keeps a local flag `reserveAttempted`, set just before the reserve call is issued and cleared only if Availability answers with a definite refusal (`409`, `404`), which becomes a stored `4xx` outcome. On a `5xx`/`503`:

- `reserveAttempted` is **false** (the failure was reading the slot) -> `IIdempotencyRepository.RemoveAsync(record)`, so the key can be retried at once _(Req 6.7)_.
- `reserveAttempted` is **true** -> the outcome of the reserve is unknown (Availability may have committed before the response was lost). The handler makes one best-effort `ReleaseAsync(slotId, appointmentId)` and **leaves the record `InProgress` with its `AppointmentId`**. A same-key retry before the 2-minute lease ends gets `409` in progress; after it, `ClaimAsync` takes the record over and `Claimed` carries the **same appointment id**, so the retry's reserve finds the existing reservation (if the release failed) or makes it afresh (if it succeeded). Reserve is idempotent, so nothing leaks and nothing is counted twice _(Req 6.7, 6.8)_.

In both cases the handler returns `Result.Failure(new Error(503, ...))`; nothing is stored as an outcome.

The handler never throws for an expected outcome; only a database failure at step 7 or the repository's own failures surface as exceptions.

#### `GetAppointmentQueryHandler`

Load with no tracking. Missing → `404`. Role `Staff` or `Admin` → success. Role `Customer` and `CustomerId == CallerId` → success. Anything else → the **same** `404` with the same body _(Req 7.3)_.

#### `CancelAppointmentCommandHandler`

1. Load tracked. Missing → `404`. Caller is not the owner and not `Admin` → the same `404` _(Req 8.2)_.
2. `Cancelled` → skip to 5 _(Req 8.4)_.
3. `StartUtc <= now` → `409` _(Req 8.3)_.
4. `appointment.Cancel(now)`; `SaveChangesAsync`.
5. `IAvailabilityClient.ReleaseAsync(slotId, appointmentId)`. Failure → `Result.Failure(503)` _(Req 8.5)_. Success → `Result.Success(true)` → `204`.

`Appointment.Cancel` throws `InvalidOperationException` unless the status is `Booked`; the handler's checks mean that is a bug, not a user error. A concurrent double cancel can both read `Booked`; the second `SaveChangesAsync` is harmless because both write the same state, and both then release, which is idempotent.

### Booking: abstractions

```csharp
public interface IAppointmentRepository
{
    Task<Appointment?> GetByIdAsync(Guid id, CancellationToken ct);                      // tracked
    Task<Appointment?> GetByIdNoTrackingAsync(Guid id, CancellationToken ct);
    Task<bool> HasOverlappingBookedAsync(Guid customerId, DateTime startUtc, DateTime endUtc, CancellationToken ct);
    Task<AddAppointmentOutcome> TryAddBookedAsync(Appointment appointment, IdempotencyRecord completedRecord, CancellationToken ct);
    Task SaveChangesAsync(CancellationToken ct);
}
public enum AddAppointmentOutcome { Saved, Overlap }

public interface IIdempotencyRepository
{
    Task<ClaimResult> ClaimAsync(Guid userId, string key, string requestHash, Guid appointmentId, DateTime nowUtc, CancellationToken ct);
    Task CompleteAsync(IdempotencyRecord record, int statusCode, string responseBody, DateTime nowUtc, CancellationToken ct);
    Task RemoveAsync(IdempotencyRecord record, CancellationToken ct);
}

public interface IAvailabilityClient
{
    Task<Result<SlotInfo>> GetSlotAsync(Guid slotId, CancellationToken ct);              // 404 → Failure(404); outage → Failure(503)
    Task<Result<bool>> ReserveAsync(Guid slotId, Guid appointmentId, CancellationToken ct);   // 409 → Failure(409, reason)
    Task<Result<bool>> ReleaseAsync(Guid slotId, Guid appointmentId, CancellationToken ct);
}

public sealed record SlotInfo(Guid Id, Guid BranchId, Guid ServiceTypeId, DateTime StartUtc, DateTime EndUtc, int Capacity, int ReservedCount, int AvailableCapacity);
```

`ClaimResult` is a small discriminated record (`Claimed`, `Replay`, `HashMismatch`, `InProgress`).

Using `Result` for the client keeps expected remote outcomes as values, as the project convention asks; `AvailabilityClient` turns `HttpRequestException`, `TimeoutRejectedException` and `BrokenCircuitException` into `Failure(503)` and never leaks a Polly type into Application.

### Booking: validation

| Validator | Rules |
|---|---|
| `CreateAppointmentCommandValidator` | `SlotId` not empty; `IdempotencyKey` not null, not whitespace, length at most 128 _(Req 5.3, 6.1)_ |

Get and cancel have no validator: a malformed `{id}` is rejected by the `:guid` route constraint with a `404`.

### Booking → Availability client _(Req 9)_

`AddInfrastructure` registers:

```csharp
services.AddHttpClient<IAvailabilityClient, AvailabilityClient>(client =>
{
    client.BaseAddress = new Uri(baseUrl);                       // Services:Availability:BaseUrl
    client.DefaultRequestHeaders.Add(Constants.ApiKeyHeaderName, apiKey);   // InternalApi:Key
})
.AddStandardResilienceHandler(o =>
{
    o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
    o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(20);
    // retry: defaults (3 attempts, exponential backoff with jitter) — POST included
    // circuit breaker: defaults
});
```

- `AddStandardResilienceHandler` (package `Microsoft.Extensions.Http.Resilience`) supplies the timeouts, retry with backoff and jitter, and circuit breaker of BRD 11.4 in one call. Its retry strategy retries every HTTP method by default, **including POST**. That is deliberate here, and safe only because reserve and release are idempotent per appointment _(Req 9.2)_; the comment in the registration says so, because adding a non-idempotent call to this client later would be wrong.
- The 20-second total timeout bounds the whole retry sequence, and the 2-minute idempotency lease (Req 6.8) is far longer than that, so a live request never looks abandoned.
- Both settings are read and validated in `AddInfrastructure` before the client is built, so a missing value stops startup _(Req 9.6)_. The key is added as a default header and never logged; `LoggingMiddleware` logs only method and path.
- `AvailabilityClient` maps: `200` → value; `204` → success; `404` → `Failure(404, "Slot not found.")`; `409` → `Failure(409, details)` read from the `Error` body Availability returns; `401`/`403` → log error, `Failure(503)` _(Req 9.5)_; anything else, including an exhausted retry, an open circuit or a timeout → `Failure(503, "The availability service is unavailable. Try again later.")`.

## Data model

### Availability

```csharp
public sealed class SlotReservation
{
    public Guid SlotId { get; private set; }
    public Guid AppointmentId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public static SlotReservation Create(Guid slotId, Guid appointmentId, DateTime createdAtUtc); // guards empty ids
}
```

`SlotReservations` table: composite primary key `(SlotId, AppointmentId)`, which is the unique constraint of Req 3.3; foreign key to `Slots` with `Restrict`. `AppointmentId` has no foreign key: it points into another service's database. The entity exists for the model, the migration and the domain test; the repository inserts through SQL (`ON CONFLICT`) because EF has no portable `DO NOTHING`.

`Slots` gains two check constraints in `OnModelCreating`: `CK_Slots_ReservedCount_NonNegative` (`"ReservedCount" >= 0`) and `CK_Slots_ReservedCount_WithinCapacity` (`"ReservedCount" <= "Capacity"`) _(Req 3.7)_. The comment on `Slot.ReservedCount` is rewritten from "Always 0 until booking reserves capacity." to say that it is the number of live `SlotReservation` rows for the slot, changed only by `SlotReservationRepository` with conditional updates. Migration `AddSlotReservations` covers both. There is no backfill: every existing `ReservedCount` is 0.

### Booking

```csharp
public enum AppointmentStatus { Booked, Cancelled }

public sealed class Appointment
{
    public Guid Id { get; private set; }               // Guid v7, generated by Booking
    public Guid CustomerId { get; private set; }
    public Guid SlotId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ServiceTypeId { get; private set; }
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public AppointmentStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }

    public static Appointment Book(Guid id, Guid customerId, SlotInfo-like fields, DateTime nowUtc);  // guards: empty ids, EndUtc > StartUtc
    public void Cancel(DateTime nowUtc);    // Booked -> Cancelled, sets CancelledAtUtc and UpdatedAtUtc; throws if not Booked
}
```

`Appointment.Book` takes the individual slot fields (not `SlotInfo`) because Domain cannot reference Application. The id is a parameter because the handler generated it when it claimed the key _(decision 5)_.

```csharp
public sealed class IdempotencyRecord
{
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string IdempotencyKey { get; private set; }     // max 128
    public string RequestHash { get; private set; }        // 64 hex chars
    public Guid AppointmentId { get; private set; }        // the id this request will give its appointment
    public IdempotencyState State { get; private set; }    // InProgress, Completed
    public int? StatusCode { get; private set; }
    public string? ResponseBody { get; private set; }      // jsonb in PostgreSQL
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public static IdempotencyRecord Claim(Guid userId, string key, string hash, Guid appointmentId, DateTime nowUtc);  // InProgress, ExpiresAt = now + 2 min lease
    public void Complete(int statusCode, string responseBody, DateTime nowUtc);   // Completed, ExpiresAt = now + 24 h
    public bool IsExpired(DateTime nowUtc);
}
```

This is the BRD 11.1 table with `AppointmentId` and `State` added. The **expiry carries two meanings**: for an `InProgress` record it is the end of the lease (2 minutes after the claim), for a `Completed` one it is the end of the 24-hour replay window _(Req 6.8, 6.9)_. One column, one question — "is this record still binding?" — keeps the claim query simple.

EF configuration:

| Table | Configuration |
|---|---|
| `Appointments` | key `Id`; `Status` stored as string (max 16) via `HasConversion<string>()`; `StartUtc`/`EndUtc`/`CreatedAtUtc`/`UpdatedAtUtc` required; `CancelledAtUtc` nullable; index `IX_Appointments_CustomerId_StartUtc` (overlap check and the future "my appointments") |
| `IdempotencyRecords` | key `Id`; **unique index `IX_IdempotencyRecords_UserId_IdempotencyKey`**; `IdempotencyKey` max 128; `RequestHash` max 64; `State` as string; `ResponseBody` column type `jsonb` |

Booking has no foreign key to Availability's data; `SlotId`, `BranchId` and `ServiceTypeId` are plain GUIDs, copies taken at booking time, which is also what an `AppointmentBooked` event will carry later. Migration `InitialCreate`. The database is `smart_appointment_booking`.

### Claiming and taking over a key

`IdempotencyRepository.ClaimAsync`:

1. Add the new record and `SaveChanges`. Success → `Claimed`. A unique violation on the index (checked as `UniqueViolation.IsOn(ex, IdempotencyRepository.KeyIndexName)`, copied from Availability's helper) means the key exists: the loser of the race reads the winner's row, which is how "two simultaneous first requests" are decided by the database _(Req 6.5)_.
2. Load the existing row for `(UserId, Key)` with no tracking:
   - **not expired**, hash differs → `HashMismatch`;
   - not expired, hash equal, `Completed` → `Replay(StatusCode, ResponseBody)`;
   - not expired, hash equal, `InProgress` → `InProgress`;
   - **expired** → take it over with one conditional statement: `ExecuteUpdateAsync` setting `RequestHash`, `State = InProgress`, `StatusCode = null`, `ResponseBody = null`, `CreatedAtUtc = now`, `ExpiresAtUtc = now + lease`, and `AppointmentId` set to the **old** id when the old state was `InProgress` (a crashed first attempt may have reserved it) or to the new one when it was `Completed`, `WHERE Id = @id AND ExpiresAtUtc = @observedExpiry`. One row affected → `Claimed`; none → another request took it first → `InProgress`. `Claimed` carries the record as it now stands, so the handler uses `record.AppointmentId`, not its own fresh GUID.
3. If the row vanished between the failed insert and the read (the other request failed and removed it), loop once from step 1.

`RemoveAsync` deletes by `Id` and state, so a slow request cannot delete a record a later request has taken over.

### The overlap race (Req 5.9)

The pre-check in step 5 and the save in step 7 are separated by the remote reserve call, so two requests by the same customer with **different** idempotency keys (a double click on two tabs, a script) could both pass the check. **Decision:** close it with a per-customer advisory lock and a second check at save time.

`IAppointmentRepository.TryAddBookedAsync` runs in one transaction that first takes `SELECT pg_advisory_xact_lock(hashtextextended(@customerId::text, 0))`, then **re-runs the overlap query**, then inserts the appointment and completes the idempotency record, then commits; the lock is released with the transaction. If the re-check finds an overlap it rolls back and returns `Overlap`, and the handler releases the reservation and answers `409` (step 7). The lock is held only for those few local statements, **never across the HTTP call**, so an Availability outage cannot hold it. Different customers use different lock keys and do not contend (a hash collision merely serialises two customers briefly).

The cost is one extra round trip and, for the loser, a reserve that is immediately released; the customer-visible result is correct either way. The handler's behaviour on `Overlap` is unit-tested through the repository abstraction; the lock and the SQL wait for the integration harness and are checked by hand in the last task (two parallel creates with different keys for overlapping slots must give one `201` and one `409`, and the loser's slot must be back at its prior `ReservedCount`).

## Error handling

| Condition | `Error.Status` | Controller result | Req |
|---|---|---|---|
| Missing/blank/long `Idempotency-Key`, empty `SlotId`, empty `AppointmentId` | 400 | `400` (not stored) | 5.3, 6.1, 3.6, 4.4 |
| No valid token / bad API key / JWT on `/internal` | — | `401` (authentication) | 1.2–1.4, 7.4 |
| Wrong role | — | `403` (authorization) | 5.10 |
| Slot unknown (Availability, or Booking's read of it) | 404 | `404` (stored for create) | 2.2, 3.5, 4.4, 5.4 |
| Appointment unknown or not yours | 404 | `404`, identical body | 7.3, 8.2 |
| Slot started, branch or service inactive, slot full | 409 | `409` (stored for create) | 3.4, 5.6 |
| Customer already booked then | 409 | `409` (stored) | 5.5 |
| Key in progress | 409 | `409` (not stored) | 6.5 |
| Cancel after start | 409 | `409` | 8.3 |
| Same key, different body | 422 | `422` | 6.4 |
| Availability unreachable, circuit open, key mismatch | 503 | `503` (claim removed if no reserve was attempted, otherwise left in progress) | 5.8, 6.7, 8.5, 9.3, 9.5 |
| Failure saving the appointment | — | `ExceptionMiddleware` `500` (reservation released best effort, claim left in progress) | 5.7, 6.7 |

Availability returns the shared `Error` body, so the `Details` of its `409` reaches the customer unchanged.

## Testing strategy

| Test class | Project | Covers |
|---|---|---|
| `ApiKeyAuthenticationTests` | BuildingBlocks.Tests | handler: missing header, wrong key, equal key, different-length key, principal has no role claims (Req 1.1-1.5); `AddApiKeyAuthentication`: missing, blank and short key stop startup (1.6); the policy accepts only the API-key scheme and `InternalServicePolicy` is not satisfied by a role (1.3, 1.4) |
| `ErrorResultExtensionsTests` (additions) | BuildingBlocks.Tests | `422` and `503` reach the caller (10.6) |
| `SlotReservationTests` | Availability.Tests | `Create` guards empty ids |
| `GetInternalSlotHandlerTests`, `ReserveSlotCommandHandlerTests`, `ReleaseSlotCommandHandlerTests` | Availability.Tests | 404 paths; each 409 reason (slot started with a pinned `TimeProvider`, branch inactive, service inactive, full); repeat reserve returns success **without** consulting state; reserve never calls `ReserveAsync` when a state check fails; release makes no state checks; repository outcomes map to results (3, 4) |
| `ReserveSlotCommandValidatorTests`, `ReleaseSlotCommandValidatorTests` | Availability.Tests | empty ids |
| `InternalSlotsControllerTests` | Availability.Tests | status mapping; class-level `InternalServicePolicy` read by reflection (1.3) |
| `AppointmentTests`, `IdempotencyRecordTests` | Booking.Tests | `Book` guards; `Cancel` only from `Booked`, sets timestamps; `Claim`/`Complete` set state and the 2-minute and 24-hour expiries; `IsExpired` at the boundary |
| `CreateAppointmentCommandValidatorTests` | Booking.Tests | key missing, blank, 128 and 129 characters; empty slot id |
| `CreateAppointmentCommandHandlerTests` | Booking.Tests | happy path (calls in order, appointment saved, `201`, outcome stored); `Replay` returns stored success and stored failure without calling Availability; `HashMismatch` → 422; `InProgress` → 409; slot 404 stored; overlap → 409 and no reserve; Availability 409 forwarded and stored; `503` while reading the slot removes the claim and stores nothing; `503` at the reserve call makes one compensation release, leaves the claim in progress (`RemoveAsync` never called, nothing stored) and returns 503; a definite Availability `409`/`404` at reserve is stored and does not count as an attempt; save exception → release called, claim left in progress, exception rethrown; `Overlap` from `TryAddBookedAsync` → release called and `409` stored (and `503` with the claim left in progress if that release fails); a retry whose `ClaimAsync` returns `Claimed` with an earlier appointment id reserves with **that** id, not a fresh one; `InProgress` from the repository → 409 |
| `GetAppointmentHandlerTests` | Booking.Tests | owner, staff, admin succeed; other customer and unknown get identical `404` |
| `CancelAppointmentHandlerTests` | Booking.Tests | owner and admin cancel; staff non-owner `404`; after start `409`; already-Cancelled re-releases and succeeds even after start; release failure → `503` with the appointment still `Cancelled` |
| `AvailabilityClientTests` | Booking.Tests | with a fake `HttpMessageHandler`: status mapping, header sent, `HttpRequestException`/timeout/broken circuit → `503`, `401` → `503` |
| `AppointmentsControllerTests` | Booking.Tests | `201` with `Location`; failure mapping including `422` and `503`; missing/unparseable `sub` → `401`; per-action policies by reflection |

Pure unit tests, following the existing style. The repositories' SQL — the atomic reserve/release transactions, `ON CONFLICT`, the idempotency claim and takeover, the advisory lock, the migrations — is **not** covered by an automated test: like every other repository in the solution it waits for the Testcontainers/integration harness. It is verified by hand against a local PostgreSQL in the last task, including a concurrent-reserve run against a capacity-1 slot.

## Decisions taken

The project owner settled every point that was open in the first draft.

1. **Cancel order.** Mark `Cancelled` and save, then release. A release failure returns `503`; a repeat cancel on a `Cancelled` appointment re-releases and returns `204` (Req 8.1, 8.4, 8.5). The customer's intent is recorded even while Availability is down; the cost is that the place stays held until the retry, or until reconciliation (out of scope).
2. **Overlap race.** Per-customer advisory transaction lock plus a re-check inside the save transaction, as above (Req 5.9). The exclusion-constraint alternative and the documented gap were rejected.
3. **Stranded reservations.** After a reserve was attempted, a `5xx` leaves the idempotency record `InProgress` with its `AppointmentId`; a same-key retry after the 2-minute lease takes it over and reuses that id. A failure before any reserve call removes the claim. Only success and `4xx` outcomes are stored (Req 6.7, 6.8). The residue is a client that never retries, which reconciliation covers later.
4. **Approved as drafted.** An unknown slot on create is `404` and stored. A stored `4xx` is replayed for 24 hours, so a client needs a new key for a new attempt. Release of an unknown slot is `404`. `Staff` can view but not cancel (a non-owner Staff gets `404`). Reserve and release answer `204`. The 2-minute lease, the 5 s attempt and 20 s total timeouts and the 128-character key limit stand. `422` and `503` are mapped in `ToActionResult` (task 2). The development API key is a throwaway 32+ character value, equal in both services.

5. **Circuit breaker defaults kept.** The end-to-end check showed that the standard resilience handler's circuit breaker (minimum throughput 100 in a 30 s window) does not open under 8 consecutive failing creates locally; each call fails after the 20 s total timeout. The project owner decided to keep the defaults: the circuit opens only at production-like volume, and locally a call to a down Availability fails after the total timeout.
