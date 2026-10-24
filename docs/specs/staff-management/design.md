# Staff management — Design

## Overview

Everything lands in the Auth service, on the existing admin-only `StaffController`, in the shape the service already uses: one MediatR command or query per action, a FluentValidation validator where input needs one, and `Result<T>` failures mapped with `this.ToActionResult`.

- **Lifecycle**: deactivate, reactivate and delete change the `User` through domain methods.
  - Deactivate and delete save the user and revoke their refresh tokens in one transaction. This generalises the transaction `SaveBranchMoveAsync` already runs for a move ([ADR-0009](../../adr/0009-end-sessions-on-staff-deactivation-and-deletion.md)).
  - Reactivation reuses `StaffBranchCheck` against Availability.
- **Delete** is soft: a nullable `DeletedAtUtc` column ([ADR-0008](../../adr/0008-soft-delete-staff-accounts.md)). Deleted accounts are excluded by explicit checks in the handlers that must not see them, not by a global query filter, because register, create-staff and the internal lookup must still see them.
- **List**: copies Booking's `/my`. The parameters are bound as raw strings, parsed by one static helper shared by the validator and the handler, and the result is a page record with the total.

The only non-obvious part is a race. EF Core updates only the columns a request changed, so a reactivation that read the user before a concurrent delete can leave the row deleted *and* `IsActive = true`. Sign-in is therefore decided by a domain rule, `User.CanSignIn` (active and not deleted), which login and refresh use instead of `IsActive` alone. The racy row stays harmless: it cannot sign in and is hidden everywhere a deleted account is (Req 6.3).

## Architecture

| Layer | Contents |
|---|---|
| `Auth.Api` | `StaffController`: five new actions (list, get, activate, deactivate, delete); create returns `CreatedAtAction` |
| `Auth.Application` | `ListStaffQuery`, `GetStaffQuery`, `SetStaffActiveCommand`, `DeleteStaffCommand` and their handlers; `ListStaffQueryValidator`; `StaffListParameters`; `StaffPage`; `StaffResponse` extended; `IUserRepository` members changed and added; `LoginUserHandler`, `RefreshTokenCommandHandler`, `GetCustomerByEmailHandler` and `AssignStaffBranchHandler` adjusted |
| `Auth.Domain` | `User.DeletedAtUtc`, `User.IsDeleted`, `User.CanSignIn`, `User.MarkDeleted(DateTime)`; `Activate` refuses a deleted user |
| `Auth.Infrastructure` | `UserRepository.SaveAndRevokeSessionsAsync` (renamed from `SaveBranchMoveAsync`), `ListStaffAsync`; `DeletedAtUtc` mapping; migration `AddUserDeletedAt` |

Dependencies keep their direction: Api → Application → Domain, Infrastructure → Application. Nothing is added to BuildingBlocks or BuildingBlocks.Web. MediatR finds the new handlers by assembly scanning. The one new validator is registered explicitly in `Auth.Application/Dependency/DependencyInjection.cs` (`services.AddScoped<IValidator<ListStaffQuery>, ListStaffQueryValidator>()`), as every existing validator is. `Auth.Application.csproj` gains `<InternalsVisibleTo Include="Auth.Tests" />` so the internal `StaffListParameters` can be tested directly, as Booking does for its parameter helper.

## Components and interfaces

### Endpoints

All on `StaffController` (`[Route("api/auth/staff")]`, `[Authorize(Policy = Constants.AdminPolicy)]` at class level, so every action is admin-only: `403` for Staff and Customer, `401` without a token; Req 8.1, 8.2).

| Verb / route | Action | Success | Dispatches |
|---|---|---|---|
| `GET /api/auth/staff?branchId=&isActive=&page=&pageSize=` | `List` | `200` `StaffPage` | `ListStaffQuery` |
| `GET /api/auth/staff/{id:guid}` | `Get` | `200` `StaffResponse` | `GetStaffQuery` |
| `POST /api/auth/staff` (existing) | `Create` | `201` + `Location` → `Get` | `CreateStaffCommand` |
| `PUT /api/auth/staff/{id:guid}/branch` (existing) | `AssignBranch` | `200` `StaffResponse` | `AssignStaffBranchCommand` |
| `POST /api/auth/staff/{id:guid}/deactivate` | `Deactivate` | `204` | `SetStaffActiveCommand(id, false)` |
| `POST /api/auth/staff/{id:guid}/activate` | `Activate` | `204` | `SetStaffActiveCommand(id, true)` |
| `DELETE /api/auth/staff/{id:guid}` | `Delete` | `204` | `DeleteStaffCommand(id)` |

- **`List`** binds `[FromQuery] string? branchId, isActive, page, pageSize` and passes each through a private `Raw(name, bound)` helper, copied from Booking's `AppointmentsController`. It turns `?isActive=` (which MVC binds to null) back into `""`, so an empty value is invalid rather than omitted (Req 2.10). Failures go through `this.ToActionResult`.
- **`Get`** is named for `CreatedAtAction(nameof(Get), new { id = value.Id }, value)` in `Create`, which replaces `StatusCode(201, ...)` and its "No staff GET exists" comment (Req 3.4). The gateway's existing `RequestHeaderOriginalHost` transform puts the gateway's host in `Location`.
- **`{id:guid}`**: a non-GUID id does not match the route and gets `404` (Req 3.3).
- **Activate, deactivate and delete** return `NoContent()` on success, as `BranchesController.SetActive` does.
- Each new action gets `[ProducesResponseType]` attributes like the existing ones, so the OpenAPI document and Scalar show them (Req 8.5).

### Commands, queries and handlers

**`ListStaffQuery(string? BranchId, string? IsActive, string? Page, string? PageSize) : IRequest<Result<StaffPage>>`.** The values are raw strings, with the same doc comment as Booking's `ListMyAppointmentsQuery`.

`ListStaffHandler(IUserRepository, IValidator<ListStaffQuery>)`:
1. Validates the query. On failure it returns `Error(400, "Invalid request data. Errors: <every message>")` (Req 2.10).
2. Parses the four values with `StaffListParameters`. The validator accepted them, so the parses cannot fail, and defaults apply to omitted values.
3. Calls `ListStaffAsync(branchId, isActive, page, pageSize)`.
4. Returns `StaffPage(items.Select(StaffResponse.From), page, pageSize, totalCount)` (Req 2.1, 2.9).

**`StaffListParameters`** (internal static, `Auth.Application/Models`) mirrors `MyAppointmentsParameters`:

| Method | Accepts | Rejects / defaults |
|---|---|---|
| `TryParseBranchId` | null (no filter); `Guid.TryParse` | `Guid.Empty`; anything else |
| `TryParseIsActive` | null (no filter); `"true"` or `"false"`, ordinal ignore-case | anything else, e.g. `"1"`, `""` |
| `TryParsePage` | digits-only whole number ≥ 1 | omitted → `DefaultPage = 1` |
| `TryParsePageSize` | digits-only whole number 1..`MaxPageSize = 100` | omitted → `DefaultPageSize = 20` |

`TryParseIsActive` uses explicit comparisons, not `bool.TryParse`, which also trims whitespace. Page and page size use `NumberStyles.None` (digits only), so `+1`, `1.0` and `-1` are invalid. Together these satisfy Req 2.3, 2.5, 2.9 and 2.10.

**`GetStaffQuery(Guid Id) : IRequest<Result<StaffResponse>>`.** `GetStaffHandler` reads `GetByIdAsync` (no tracking). It returns `404 "Staff member '{id}' was not found."` unless the user exists, has role `Staff` and is not deleted (Req 3.1, 3.2, 6.3, 8.3).

**`SetStaffActiveCommand(Guid Id, bool IsActive) : IRequest<Result<Unit>>`.** `SetStaffActiveHandler(IUserRepository, IAvailabilityClient, TimeProvider, ILogger)`:
1. Reads `GetForUpdateByIdAsync`. If it is null, not `Staff`, or `IsDeleted`, the result is `404` (Req 4.5, 5.5, 8.3).
2. **Deactivate:**
   - Calls `user.Deactivate()`, a no-op when the user is already inactive.
   - Calls `SaveAndRevokeSessionsAsync(user, now)` in both cases, so a repeat still revokes any live token (Req 4.1, 4.2).
   - There is no Availability call (Req 4.6).
3. **Activate:**
   - If the user is already active, returns success with no Availability call and no save (Req 5.4).
   - Otherwise runs `StaffBranchCheck.CheckAsync(user.BranchId!.Value)` and returns its `422` or `503` unchanged, leaving the user unsaved (Req 5.1).
   - Then calls `user.Activate()` and plain `SaveChangesAsync`, with no revocation, and no session comes back (Req 5.2, 5.3).
4. Logs the change and the number of tokens revoked, as the move handler does.

No validator: the only input is a route GUID, and `{id:guid}` plus the 404 rule cover it. This follows `SetBranchActiveCommandHandler`, which has none either.

**`DeleteStaffCommand(Guid Id) : IRequest<Result<Unit>>`.** `DeleteStaffHandler(IUserRepository, TimeProvider, ILogger)`:
1. Reads `GetForUpdateByIdAsync`. If it is null, not `Staff`, or already deleted, the result is `404`, so a second `DELETE` is `404` (Req 6.3, 6.7).
2. Calls `user.MarkDeleted(now)`, then `SaveAndRevokeSessionsAsync(user, now)` (Req 6.1, 6.2).

No validator, as above.

**Changes to existing handlers:**

| Handler | Change | Req |
|---|---|---|
| `AssignStaffBranchHandler` | The 404 condition gains `\|\| user.IsDeleted`. It calls `SaveAndRevokeSessionsAsync` (the rename). No `IsActive` check is added: an inactive account may be moved and stays inactive. | 7.1, 7.2, 7.3 |
| `LoginUserHandler` | `!user.IsActive` becomes `!user.CanSignIn`. The `401` is unchanged. | 4.3, 6.3 |
| `RefreshTokenCommandHandler` | `!user.IsActive` becomes `!user.CanSignIn`, revoking the family as today. | 4.3, 6.3 |
| `GetCustomerByEmailHandler` | `user is null` becomes `user is null \|\| user.IsDeleted`, giving the same `404 "Customer not found"`. | 6.3 |
| `CreateStaffHandler`, `RegisterCustomerCommandHandler` | Unchanged. `GetByEmailAsync` still finds a deleted account, so the existing `400 "Email is already registered."` applies. | 6.4 |
| `LookupUsersHandler` | Unchanged. `ListByIdsAsync` still returns deleted users. | 6.5 |

`GetMe` and other token-authenticated reads are unchanged (Req 6.6).

### Models

- **`StaffResponse`** gains `bool IsActive` and `DateTime RegisteredAtUtc` after `BranchId`. `From(user)` maps `user.IsActive` and `user.RegistrationDateUtc`. The JSON names are `isActive` and `registeredAtUtc`, serialised as UTC ISO 8601 because the value is stored and read as UTC. No field is removed, and the deletion time is not exposed (Req 1.1, 1.2, 1.3). Create and move return it unchanged.
- **`StaffPage(IReadOnlyList<StaffResponse> Items, int Page, int PageSize, int TotalCount)`**: an Auth-local copy of Booking's `PagedResponse<T>` shape, non-generic because it has one use (Req 2.1).

### Abstractions

`IUserRepository` changes:

```csharp
/// Saves the user's pending change and revokes every live refresh token of that user in one
/// transaction (both or neither). Returns the number of tokens revoked. Used by move, deactivate, delete.
Task<int> SaveAndRevokeSessionsAsync(User user, DateTime nowUtc, CancellationToken cancellationToken); // was SaveBranchMoveAsync

/// Non-deleted users with role Staff, optionally by branch and active flag (AND), ordered by
/// RegistrationDateUtc then Id, one page plus the total across all pages. No tracking.
Task<StaffListPage> ListStaffAsync(Guid? branchId, bool? isActive, int page, int pageSize, CancellationToken cancellationToken);
```

`StaffListPage(IReadOnlyList<User> Items, int TotalCount)` is declared beside the interface, as Booking's `AppointmentPage` is.

**`ListStaffAsync`** builds one `IQueryable`: `Role == Staff && DeletedAtUtc == null`, plus the optional filters. It runs `CountAsync`, then `OrderBy(RegistrationDateUtc).ThenBy(Id).Skip((page-1)*pageSize).Take(pageSize).ToListAsync()`. Only the page and the count are read (Req 2.2–2.8, 2.11). These are two queries, as Booking does. A staff member created between them can make the count one off, which is accepted. As in Booking's `ListForCustomerAsync`, the skip is computed as a `long` (a page near `int.MaxValue` would overflow an `int`), and when it is at or past the total the page query is skipped and an empty page is returned with the real total (Req 2.9).

**`SaveAndRevokeSessionsAsync`** keeps the body of `SaveBranchMoveAsync` (transaction, `SaveChangesAsync`, then `ExecuteUpdateAsync` setting `RevokedAtUtc` on the user's unrevoked tokens) and its comment about the execution strategy. Only the name and doc comment change.

## Data model

`User` (Domain):

```csharp
/// When an admin deleted this staff account; null while it exists. The row is kept (ADR-0008).
public DateTime? DeletedAtUtc { get; private set; }

public bool IsDeleted => DeletedAtUtc is not null;

/// Login and refresh are allowed only for an active account that is not deleted.
public bool CanSignIn => IsActive && !IsDeleted;

public void MarkDeleted(DateTime nowUtc)   // throws InvalidOperationException unless Role == Staff
{                                          // and not already deleted
    DeletedAtUtc = nowUtc;
    IsActive = false;
}
```

- **`Activate()`** throws `InvalidOperationException` when the user is deleted. Handlers never reach this, because they return `404` first, so the throw guards the invariant rather than reporting a failure (Req 6.8).
- **`Deactivate()`** is unchanged.
- **`MarkDeleted`** takes the instant from the handler's `TimeProvider`, unlike the older methods, so tests can pin it.

EF Core configuration in `ApplicationDbContext`: `entity.Property(e => e.DeletedAtUtc);` (nullable `timestamp with time zone`). `IsDeleted` and `CanSignIn` are computed, so EF ignores them because they have no setter. Add `entity.Ignore(...)` only if the model builder complains.

**Migration `AddUserDeletedAt`** adds one nullable column, `Users.DeletedAtUtc`, with no default and no backfill. It is generated with `dotnet ef migrations add AddUserDeletedAt --project src/Services/Auth/Auth.Infrastructure --startup-project src/Services/Auth/Auth.Api`, which needs no database thanks to the design-time factory. There is no index: staff accounts number in the tens (see Out of scope in the requirements).

## Concurrency

| Race | Outcome | Why it is acceptable |
|---|---|---|
| Login vs deactivate or delete | A login that passed the `CanSignIn` check may store a new family after the revocation. | Refresh refuses the user (`CanSignIn` false) and revokes that family. A repeat deactivate revokes it as well (Req 4.2). |
| Activate vs delete | The row may end deleted with `IsActive = true`. | `CanSignIn` is false and every handler treats it as deleted (Req 6.3). |
| Activate vs deactivate | The last writer wins. | Either end state is a valid admin outcome. Sessions were revoked by the deactivation either way. |
| Move vs delete | The deleted row may get the new `BranchId`. | It stays deleted and hidden, so the change is harmless. |
| Delete vs delete | Both requests can pass the not-deleted check and both return `204`; the later `DeletedAtUtc` wins. | The account ends deleted with its sessions revoked either way. Only the recorded instant may be a moment later, and a sequential second `DELETE` is still `404` (Req 6.3). |

There is no optimistic concurrency token. Adding one to `User` would affect every user write, and none of these races breaks a requirement.

## Error handling

| Condition | `Error.Status` | Controller result |
|---|---|---|
| List: any bad query value (all listed at once) | 400 | `ToActionResult` → problem+json |
| Get, activate, deactivate, delete or move: unknown id, non-staff (including admin) or deleted | 404 | `ToActionResult` |
| Activate: branch unknown or inactive | 422 (from `StaffBranchCheck`) | `ToActionResult` |
| Activate: Availability unreachable or 5xx | 503 (from `IAvailabilityClient`) | `ToActionResult` |
| Login or refresh for a user who cannot sign in | 401 (existing messages) | `ToActionResult` |
| Profile by email of a deleted account | 404 (existing message) | `ToActionResult` |
| Register or create-staff with a deleted account's email | 400 `Email is already registered.` (existing) | `ToActionResult` |
| `Activate()` or `MarkDeleted()` invariant broken | exception | `ExceptionMiddleware` → 500 (not reachable through the handlers) |

## Gateway and configuration

There are no gateway changes. `/api/auth/{**catch-all}` routes every new path and method with the JWT checked at the edge, none of them is rate-limited, and the CORS policy already allows `DELETE` (Req 8.4). No new configuration keys are needed: reactivation uses the existing `Services:Availability:BaseUrl` and `InternalApi:Key`.

## Testing strategy

Tests go in `tests/Auth.Tests`, in the existing xUnit + Moq style: handlers and controllers built directly, repositories mocked, and assertions on `Result` values and concrete `IActionResult` types.

| Test file | Covers |
|---|---|
| `UserTests` (extend) | `MarkDeleted` sets `DeletedAtUtc` and `IsActive = false`, and throws for a non-staff or already deleted user; `CanSignIn` for active, inactive and deleted users; `Activate` throws when deleted (Req 6.1, 6.8) |
| `StaffListParametersTests` (new) | Each parser's accepted, rejected and default values, including `+1`, `1.0`, `-1`, `""`, `TRUE`, `1` and `Guid.Empty` (Req 2.9, 2.10) |
| `ListStaffQueryValidatorTests` (new) | One message per bad parameter, and `isActive=x&branchId=xyz&page=0` giving three messages (Req 2.10) |
| `ListStaffHandlerTests` (new) | Parsed filters and paging reach `ListStaffAsync`; defaults; mapping to `StaffPage` with `totalCount`; validation `400` without calling the repository (Req 2.1, 2.3–2.6, 2.9) |
| `GetStaffHandlerTests` (new) | `200` with the shape; `404` for unknown, customer, admin and deleted ids; inactive returned (Req 3.1, 3.2, 8.3) |
| `SetStaffActiveHandlerTests` (new) | Deactivate calls `SaveAndRevokeSessionsAsync`, also when already inactive, and never Availability. Activate checks the branch: `422` and `503` leave the user inactive and unsaved; success saves without revocation; already active means no Availability call and no save. `404` cases (Req 4.1, 4.2, 4.5, 4.6, 5.1–5.5) |
| `DeleteStaffHandlerTests` (new) | Marks the user deleted and calls `SaveAndRevokeSessionsAsync`; an active user is deleted directly; `404` for unknown, non-staff and already deleted users (Req 6.1, 6.3, 6.7) |
| `AssignStaffBranchHandlerTests` (update) | The rename; `404` for a deleted user; moving an inactive user succeeds and leaves it inactive (Req 7.1, 7.2) |
| `LoginUserHandlerTests`, `RefreshTokenCommandHandlerTests` (extend) | A deleted user gives `401`, and a deleted user with `IsActive = true` (the race) also gives `401` (Req 4.3, 6.3) |
| `GetCustomerByEmailHandlerTests` (extend) | A deleted account gives `404` (Req 6.3) |
| `CreateStaffHandlerTests`, `RegisterCustomerCommandHandlerTests` (extend) | A deleted account's email gives `400 "Email is already registered."` (Req 6.4) |
| `LookupUsersHandlerTests` (extend) | A deleted user is still returned (Req 6.5) |
| `StaffControllerTests` (extend) | `List` returns `OkObjectResult` and passes the raw strings, including `""` for an empty value; `Get` returns `200` or `404`; activate, deactivate and delete return `NoContentResult` or the problem body; `Create` returns `CreatedAtActionResult` naming `Get` with the id (Req 3.4); the class-level `AdminPolicy` attribute covers the new actions (Req 8.1) |

The repository queries (role and deleted filters, sort, paging, the transaction) have no database-backed tests in this project. The manual end-to-end check in the plan covers them through the gateway, along with `401` and `403` for real tokens (Req 2.2, 2.8, 2.11, 8.1, 8.2).

## Decisions recorded

- [ADR-0008](../../adr/0008-soft-delete-staff-accounts.md): soft-delete staff accounts (Accepted).
- [ADR-0009](../../adr/0009-end-sessions-on-staff-deactivation-and-deletion.md): end all sessions when a staff account is deactivated or deleted (Accepted).

Routine choices that follow existing conventions get no ADR. These are the raw-string paging parameters, the Auth-local page record, explicit deleted checks (recorded inside ADR-0008), `CanSignIn` and the `204` lifecycle responses.
