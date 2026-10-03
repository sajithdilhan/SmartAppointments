# Availability service types — Design

> References like _(Req 1.3)_ point at [`requirements.md`](requirements.md).

## Overview

`ServiceType` is a second aggregate in the Availability service, built the same way as `Branch` and following [`availability-branches/design.md`](../availability-branches/design.md) step for step: a GUID surrogate key plus a unique, immutable, upper-cased `Code`; a no-tracking duplicate pre-check backed by a unique index; repository staging and committing as separate steps; and role narrowing in the handler, using the role the controller reads from the token. Where this design says "as for branches", the branch design applies with `Branch` read as `ServiceType`.

## Components

### Endpoints

`ServicesController`, `[Route("api/[controller]")]`, so the routes are `/api/Services/...`, matched case-insensitively.

| Verb / route | Action | Authorization | Dispatches | Req |
|---|---|---|---|---|
| `POST /api/services` | `Create` | `AdminPolicy` | `CreateServiceTypeCommand` | 1 |
| `PUT /api/services/{id:guid}` | `Update` | `AdminPolicy` | `UpdateServiceTypeCommand` | 2 |
| `POST /api/services/{id:guid}/activate` | `Activate` | `AdminPolicy` | `SetServiceTypeActiveCommand(Id, true)` | 3 |
| `POST /api/services/{id:guid}/deactivate` | `Deactivate` | `AdminPolicy` | `SetServiceTypeActiveCommand(Id, false)` | 3 |
| `GET /api/services?includeInactive=` | `GetServiceTypes` | `AllowedOriginsPolicy` | `GetServiceTypesQuery(IncludeInactive, CurrentUserRole)` | 4 |
| `GET /api/services/{id:guid}` | `GetServiceType` | `AllowedOriginsPolicy` | `GetServiceTypeQuery(Id, CurrentUserRole)` | 4 |

`Create` returns `CreatedAtAction(nameof(GetServiceType), ...)`. Failures go through the shared `ToActionResult`.

### Commands, queries and handlers

| Request | Returns | Notes |
|---|---|---|
| `CreateServiceTypeCommand(Code, Name, Description, DurationMinutes)` | `Result<ServiceTypeResponse>` | Validate → normalise code → `ExistsByCodeAsync` → stage → commit, catching `DuplicateServiceTypeCodeException` → 409 |
| `UpdateServiceTypeCommand(Id, Name, Description, DurationMinutes)` | `Result<ServiceTypeResponse>` | Validate → `GetForUpdateByIdAsync` → `UpdateDetails` → commit |
| `SetServiceTypeActiveCommand(Id, IsActive)` | `Result<bool>` | Commits only when `Activate`/`Deactivate` returned true |
| `GetServiceTypesQuery(IncludeInactive, CurrentUserRole)` | `Result<List<ServiceTypeResponse>>` | `includeInactive` honoured only for `Admin` |
| `GetServiceTypeQuery(Id, CurrentUserRole)` | `Result<ServiceTypeResponse>` | Missing, or inactive for a non-admin → the same 404 |

`ServiceTypeResponse(Id, Code, Name, Description, DurationMinutes, IsActive, CreatedAtUtc, UpdatedAtUtc)`.

The not-found error is "Service type '{id}' was not found.".

### Validation

`CreateServiceTypeCommandValidator`, and `UpdateServiceTypeCommandValidator`, which applies the same rules without `Code`:

| Field | Rule |
|---|---|
| `Code` | Not empty; after trimming matches `^[A-Za-z0-9](?:[A-Za-z0-9_-]{0,28}[A-Za-z0-9])$` |
| `Name` | Not empty, at most 100 |
| `Description` | At most 500 when not null |
| `DurationMinutes` | 5–480 inclusive, multiple of 5 |

### Abstractions

`IServiceTypeRepository` mirrors `IBranchRepository`: `AddAsync`, `ExistsByCodeAsync`, `GetByIdAsync` (no tracking), `GetForUpdateByIdAsync` (tracked), `ListAsync(includeInactive)` (no tracking, ordered by name then code), `SaveChangesAsync` (throws `DuplicateServiceTypeCodeException`).

### Telling unique violations apart

The context now has more than one unique index, so a `23505` on commit no longer identifies which constraint failed. Both repositories now check `PostgresException.ConstraintName` (`IX_Branches_Code` or `IX_ServiceTypes_Code`) before translating the violation into their duplicate-code exception. Any other unique violation is rethrown unchanged and surfaces as a 500, because it means a bug, not a duplicate code.

## Data model

```csharp
public sealed class ServiceType
{
    public Guid Id { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public int DurationMinutes { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public static ServiceType Create(string code, string name, string? description, int durationMinutes);
    public static string NormaliseCode(string code);
    public void UpdateDetails(string name, string? description, int durationMinutes);
    public bool Activate();
    public bool Deactivate();
}
```

`ServiceTypes` table: `Code` required, max 30, unique index `IX_ServiceTypes_Code`; `Name` required, max 100; `Description` max 500; `DurationMinutes` required; `IsActive`, `CreatedAtUtc` required; `UpdatedAtUtc` optional. Migration `CreateServiceTypes`.

## Testing strategy

Mirrors branches: `ServiceTypeTests` (create defaults, normalisation, update, activation idempotence), `CreateServiceTypeCommandValidatorTests` and `UpdateServiceTypeCommandValidatorTests` (each rule at its boundary), handler tests for each command and query, and `ServicesControllerTests` (status mapping, `CreatedAtAction`, the role passed into queries, and each action's policy checked by reflection). The repository and the migration are exercised by hand against a local PostgreSQL, as for branches.
