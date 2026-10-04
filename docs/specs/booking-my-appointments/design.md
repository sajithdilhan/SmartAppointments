# My appointments — Design

> References like _(Req 2.2)_ point at [`requirements.md`](requirements.md).

## Overview

One read endpoint, `GET /api/appointments/my`, added to the existing `AppointmentsController` of the Booking service. It reads only Booking's `Appointments` table. The decisions that are not obvious:

1. **No new index and no migration.** `IX_Appointments_CustomerId_StartUtc` already exists (`InitialCreate`, declared in `ApplicationDbContext`, with a comment that it serves the future "my appointments" list). It is exactly the prefix this query needs: equality on `CustomerId`, then `StartUtc` in either direction. PostgreSQL scans a btree backwards for `DESC`, so one index serves both orders. The `Id` tie-break is not in the index; PostgreSQL 13+ finishes the sort with an incremental sort over the rows that share a `StartUtc`, which is a handful at most per customer. `Status` is left out of the index: it has two values, so it would not narrow the range, and a customer's rows are few enough to filter after the index range scan. Both can be revisited if a real workload says otherwise _(Req 3.1, 3.2, 3.6; Notes for design)_.
2. **Invalid query values must reach the validator, so the controller binds raw strings.** With `[ApiController]`, a value that fails model binding (`page=abc` into an `int`) is answered by the framework with a `ValidationProblemDetails` body before the action runs. That body is not the shared `{status, detail}` error, would list only binding failures, and could not be combined with a bad `status` in one response. So `status`, `when`, `page` and `pageSize` are bound as `string?` (which cannot fail binding), carried unparsed in the query record, and parsed and checked in one place, the validator. The handler then returns a single `400` through the same `Result` path the create endpoint uses, listing every failed rule _(Req 2.4, 3.4, 3.7)_.
3. **One parsing helper, used by the validator and the handler**, so "what is valid" and "what is applied" cannot drift.
4. **A local `PagedResponse<T>`**, not a Booking-specific `AppointmentListResponse` and not a shared type. It is generic because `FR-BKG-004` (the staff view) will return the same envelope, but `BuildingBlocks` has no paging type and one consumer does not justify adding one; it moves there when a second service needs it.
5. **Two sequential queries (count, then page)** rather than one with a window function. They are simple and each is index-friendly. No transaction wraps them, so a booking made between them can make `totalCount` differ from the page by one; that is acceptable for a list view.
6. **"Now" is read once** from `TimeProvider` in the handler and passed to the repository as a `DateTime`, so the count and the page use the same instant and `upcoming`/`past` partition the customer's appointments exactly _(Req 2.2)_.

The caller's id comes only from the `sub` claim; there is no customer id parameter to read, so a `customerId` query parameter is never bound and is ignored _(Req 1.2)_.

## Architecture

| Layer | Contents |
|---|---|
| `Booking.Api` | `AppointmentsController.GetMine` (new action) |
| `Booking.Application` | `ListMyAppointmentsQuery`; `ListMyAppointmentsHandler`; `ListMyAppointmentsQueryValidator`; `MyAppointmentsParameters` (parsing helper); `PagedResponse<T>`; `AppointmentTimeFilter`; `AppointmentPage`; `IAppointmentRepository.ListForCustomerAsync`; one validator registration in `Dependency/DependencyInjection.cs` |
| `Booking.Domain` | unchanged |
| `Booking.Infrastructure` | `AppointmentRepository.ListForCustomerAsync`; no migration, no `OnModelCreating` change |
| Gateway | unchanged _(Req 4.4)_ |
| `tests/Booking.Tests` | `ListMyAppointmentsQueryValidatorTests`, `MyAppointmentsParametersTests`, `ListMyAppointmentsHandlerTests`, additions to `AppointmentsControllerTests` |

## Components and interfaces

### Endpoints

| Verb / route | Action | Authorization | Dispatches | Req |
|---|---|---|---|---|
| `GET /api/appointments/my` | `AppointmentsController.GetMine` | `CustomerPolicy` | `ListMyAppointmentsQuery` | 1, 2, 3, 4 |

```csharp
/// <summary>
/// The calling customer's appointments, filtered by <c>status</c> (Booked, Cancelled) and <c>when</c>
/// (upcoming, past), one page at a time. Upcoming is soonest first; past and unfiltered are most recent first.
/// </summary>
[HttpGet("my")]
[Authorize(Policy = Constants.CustomerPolicy)]
public async Task<IActionResult> GetMine(
    [FromQuery, DisplayFormat(ConvertEmptyStringToNull = false)] string? status,
    [FromQuery, DisplayFormat(ConvertEmptyStringToNull = false)] string? when,
    [FromQuery, DisplayFormat(ConvertEmptyStringToNull = false)] string? page,
    [FromQuery, DisplayFormat(ConvertEmptyStringToNull = false)] string? pageSize,
    CancellationToken cancellationToken)
```

Behaviour:

- `TryGetCaller` as in the other actions; a missing or non-GUID `sub` returns `Unauthorised()` (`401`) _(Req 1.3)_. The role claim is not used: `CustomerPolicy` already lets only customers in, and Staff and Admin get `403` from the framework _(Req 4.1)_. An unauthenticated request gets `401` from the authentication middleware _(Req 4.2)_.
- Sends `new ListMyAppointmentsQuery(callerId, status, when, page, pageSize)`; failure → `this.ToActionResult(result.Error!)` (the shared `{status, detail}` body); success → `Ok(result.Value)` _(Req 1.1)_.
- **Why `DisplayFormat(ConvertEmptyStringToNull = false)`.** By default MVC binds an empty query value (`?status=`) to `null`, which would make "present but empty" look like "omitted". Requirement 2.4 says an empty `status` is a `400`, and the same goes for an empty `when`, `page` or `pageSize`. The attribute keeps `""` as `""` so the validator sees it. A repeated parameter (`status=Booked&status=Cancelled`) binds to the joined string `"Booked,Cancelled"`, which is not a valid value and so is also a `400`.
- **No `customerId` parameter exists on the action**, so a supplied one is simply never read _(Req 1.2)_.
- **Routing _(Req 4.3)_.** `my` is a literal segment and `{id:guid}` is a constrained parameter; ASP.NET Core's endpoint routing ranks literal segments above parameters, so `GET /api/appointments/my` resolves to `GetMine` whatever the declaration order, and `my` never reaches `GetById` (it would fail the `:guid` constraint anyway). `GetMine` is declared above `GetById` for readability. `GET /api/appointments/{id}` is untouched.
- **OpenAPI _(Req 4.5)_.** An `[ApiController]` action with `[FromQuery]` parameters is picked up by `MapOpenApi()` automatically, like the other actions, which carry no `[ProducesResponseType]` attributes; this action follows suit and documents itself with the XML summary above only. The generated schema shows the four parameters as strings, not as integers; the summary and the `.http` examples state the real constraints (`page` ≥ 1, `pageSize` 1 to 100, default 20).

### Query, validator, handler

```csharp
public sealed record ListMyAppointmentsQuery(
    Guid CallerId, string? Status, string? When, string? Page, string? PageSize)
    : IRequest<Result<PagedResponse<AppointmentResponse>>>;
```

The query carries the raw strings (decision 2). `CallerId` is trusted: it comes from the token.

```csharp
public enum AppointmentTimeFilter { Upcoming, Past }

internal static class MyAppointmentsParameters
{
    public const int DefaultPage = 1;
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    // null input = omitted = Ok(null); "" or any other non-matching text = false.
    public static bool TryParseStatus(string? raw, out AppointmentStatus? status);   // "Booked"/"Cancelled", OrdinalIgnoreCase
    public static bool TryParseWhen(string? raw, out AppointmentTimeFilter? when);   // "upcoming"/"past", OrdinalIgnoreCase
    public static bool TryParsePage(string? raw, out int page);                      // null -> 1; int, invariant culture, >= 1
    public static bool TryParsePageSize(string? raw, out int pageSize);              // null -> 20; int, invariant culture, 1..100
}
```

- Status and `when` are matched with explicit `string.Equals(..., OrdinalIgnoreCase)` against the two names, **not** `Enum.TryParse`, which would also accept `"0"`, `"1,0"` or any member added later (`Completed`, `NoShow` must stay `400` until `FR-BKG-006` extends this list on purpose) _(Req 2.4)_. Surrounding whitespace is not trimmed, so `" booked"` is invalid.
- Integers use `int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, ...)`: digits only, so `+1`, `1.0`, `1e1` and `-1` are "not an integer" or out of range `400`s, and a value above `int.MaxValue` fails to parse and is a `400` _(Req 3.3, 3.4)_.

`ListMyAppointmentsQueryValidator : AbstractValidator<ListMyAppointmentsQuery>` has one rule per parameter, each a `Must` on the helper, all evaluated (FluentValidation's default cascade collects every failure) so one response lists them all _(Req 2.4, 3.4, 3.7)_:

| Rule | Message |
|---|---|
| `Status` fails `TryParseStatus` | `The status must be Booked or Cancelled.` |
| `When` fails `TryParseWhen` | `The when must be upcoming or past.` |
| `Page` fails `TryParsePage` | `The page must be a whole number of at least 1.` |
| `PageSize` fails `TryParsePageSize` | `The pageSize must be a whole number from 1 to 100.` |

Registered in `AddApplication()` beside the existing one: `services.AddScoped<IValidator<ListMyAppointmentsQuery>, ListMyAppointmentsQueryValidator>();`. The messages name the parameter, which is how `detail` "names every invalid parameter" _(Req 2.4)_.

`ListMyAppointmentsHandler(IAppointmentRepository appointments, IValidator<ListMyAppointmentsQuery> validator, TimeProvider timeProvider)`:

1. `validator.ValidateAsync`; on failure return `Result.Failure(new Error(400, $"Invalid request data. Errors: {string.Join(",", messages)}"))`, the format `CreateAppointmentCommandHandler` and `SearchAvailableSlotsHandler` already use _(Req 2.4, 3.4, 3.7)_. The database is not touched.
2. Resolve the parameters through `MyAppointmentsParameters` (defaults `1` and `20` applied) _(Req 3.3)_.
3. `var nowUtc = timeProvider.GetUtcNow().UtcDateTime;` exactly once _(Req 2.2)_.
4. `var page = await appointments.ListForCustomerAsync(query.CallerId, status, when, nowUtc, pageNumber, pageSize, ct);`
5. Return `Result.Success(new PagedResponse<AppointmentResponse>(page.Items.Select(AppointmentResponse.From).ToList(), pageNumber, pageSize, page.TotalCount))` _(Req 1.1, 1.4, 3.5)_.

**Mapping reuse _(Req 1.5)_.** The handler maps with the existing `AppointmentResponse.From`, the same call `GetAppointmentHandler` and the create handler use, so a list item is the single-appointment shape by construction, with `Status` serialised as a string by the existing `JsonStringEnumConverter` registration. Nothing from Availability is added, and the handler injects no client _(Req 1.7)_. A list item and the `GetById` body can never differ.

### Response model

```csharp
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
```

In `Booking.Application/Models/`. The JSON names are `items`, `page`, `pageSize`, `totalCount` by the default camel-case policy _(Req 1.1)_. `Page` and `PageSize` are the applied values, defaults included.

### Abstractions

```csharp
public sealed record AppointmentPage(IReadOnlyList<Appointment> Items, int TotalCount);

public interface IAppointmentRepository
{
    // ... existing members ...

    /// <summary>
    /// One customer's appointments, filtered, in the fixed order (soonest first when <paramref name="when"/>
    /// is Upcoming, otherwise most recent first, ties by Id ascending), one page, plus the total that
    /// match across all pages. No tracking.
    /// </summary>
    Task<AppointmentPage> ListForCustomerAsync(
        Guid customerId, AppointmentStatus? status, AppointmentTimeFilter? when, DateTime nowUtc,
        int page, int pageSize, CancellationToken cancellationToken);
}
```

`AppointmentPage` and `AppointmentTimeFilter` live in Application (`Abstractions/` and `Models/`), because Infrastructure references Application and the filter is a request concept, not a domain one. The repository takes already-parsed values, so it contains no string handling.

`AppointmentRepository.ListForCustomerAsync`:

```csharp
var query = context.Appointments.AsNoTracking().Where(a => a.CustomerId == customerId);   // always: Req 1.2
if (status is { } s)                 query = query.Where(a => a.Status == s);              // Req 2.1
if (when == Upcoming)                query = query.Where(a => a.StartUtc >= nowUtc);       // Req 2.2: at "now" is upcoming
else if (when == Past)               query = query.Where(a => a.StartUtc <  nowUtc);

var total = await query.CountAsync(ct);                                                   // query 1
var skip = (long)(page - 1) * pageSize;                                                   // long: page can be up to int.MaxValue
if (skip >= total) return new AppointmentPage([], total);                                 // beyond the last page (Req 3.5); also avoids int overflow in Skip

var ordered = when == Upcoming
    ? query.OrderBy(a => a.StartUtc).ThenBy(a => a.Id)
    : query.OrderByDescending(a => a.StartUtc).ThenBy(a => a.Id);                         // Req 3.1, 3.2
var items = await ordered.Skip((int)skip).Take(pageSize).ToListAsync(ct);                 // query 2: Req 3.6
return new AppointmentPage(items, total);
```

Because `skip < total <= int.MaxValue` once the early return passes, the `(int)` cast is safe. The early return also saves the second query for any out-of-range page. Only the requested page and the count are read; nothing is paged in memory _(Req 3.6)_. The comparison against `nowUtc` and the stored `StartUtc` are both UTC `timestamptz`, so no time zone conversion appears _(Notes for design)_. `Status` is stored as a string (`HasConversion<string>`); EF translates the enum comparison to the string column.

## Data model

No entity, configuration or migration change. The index `IX_Appointments_CustomerId_StartUtc` on `(CustomerId, StartUtc)` (btree, ascending, created by `InitialCreate`) serves the `WHERE "CustomerId" = @c` equality, the `StartUtc` range filter for `upcoming`/`past`, and the ordered scan in both directions. The existing overlap check uses it too; the new query reuses it unchanged. A covering or extended index `(CustomerId, StartUtc, Id)` or one including `Status` is deliberately **not** added (Overview 1); the verification step in the tasks should look at `EXPLAIN` for the three query shapes and confirm the index is used.

## Error handling

| Condition | `Error.Status` | Controller result | Req |
|---|---|---|---|
| Invalid `status`, `when`, `page` or `pageSize` (one or many; all listed in `detail`) | 400 | `400` via `ToActionResult`, body `{status, detail}` | 2.4, 3.4, 3.7 |
| `sub` missing or not a non-empty GUID | 401 | `401` via `ToActionResult` (`Unauthorised()`) | 1.3 |
| No valid token | — | `401` (authentication middleware) | 4.2 |
| Staff or Admin caller | — | `403` (`CustomerPolicy`) | 4.1 |
| No appointments, no matches, or page beyond the last | — | `200`, `items` empty, correct `totalCount` | 1.4, 3.5 |
| Database failure | — | `ExceptionMiddleware` `500` | — |

The handler returns `Result` failures only for the `400`; the rest never reach it. No `404` exists on this endpoint _(Req 1.4)_.

## Testing strategy

Pure unit tests, in the existing style (xUnit + Moq, concrete result types asserted, `FixedTimeProvider`, `Guid.CreateVersion7()`, `Appointment.Book(...)` to build data).

| Test class | Covers |
|---|---|
| `MyAppointmentsParametersTests` | status: `Booked`, `booked`, `CANCELLED`, null (omitted), `""`, `Completed`, `NoShow`, `1`, `" Booked"` (2.1, 2.4); when: `upcoming`, `PAST`, null, `""`, `now` (2.2, 2.4); page: null → 1, `1`, `0`, `-1`, `abc`, `1.5`, `2147483648`, `""` (3.3, 3.4); pageSize: null → 20, `1`, `100`, `0`, `101`, `abc`, `""` (3.3, 3.4) |
| `ListMyAppointmentsQueryValidatorTests` | all-valid and all-omitted pass; each rule fails alone with its message; **several bad parameters at once yield several errors** (3.7) |
| `ListMyAppointmentsHandlerTests` (repository mocked with Moq) | defaults reach the repository as page 1 and size 20, status and when null (1.1, 3.3); parsed `Booked`/`Cancelled` and `Upcoming`/`Past` reach it, case-insensitively (2.1–2.3); `callerId` from the query is the id passed, always (1.2); `nowUtc` equals the fixed clock's UTC time, and a counting `TimeProvider` proves `GetUtcNow` is called **once** (2.2); items map through `AppointmentResponse.From` field for field, `Status` as `Booked`/`Cancelled` (1.5, 1.6); response carries `page`, `pageSize`, `totalCount` as applied (1.1); empty page with `totalCount` > 0 and an empty list with `totalCount` 0 are both successes (1.4, 3.5); invalid input returns `Error(400, ...)` listing every failure and **never calls the repository** (2.4, 3.4, 3.7); no Availability dependency exists to call (1.7) |
| `AppointmentsControllerTests` (additions) | `GetMine` returns `OkObjectResult` with the `PagedResponse` (1.1); the query sent carries the `sub` GUID and the four raw strings unchanged (1.2); a failed `Result` (`400`) goes through `ToActionResult` and gives the `ObjectResult` with status `400` (2.4); missing and non-GUID `sub` → `401` and no `Send` (1.3); reflection: `GetMine` has `[HttpGet("my")]` and `[Authorize(Policy = CustomerPolicy)]` (4.1); reflection: `GetById` still has `[HttpGet("{id:guid}")]` (4.3); reflection: no parameter is named `customerId` (1.2) |

Not covered by an automated test, as for every repository in the solution (no Testcontainers yet): the translation of `ListForCustomerAsync` to SQL, the order and tie-break, `skip >= total`, and the index use. These are verified by hand in the last task against a local PostgreSQL with the gateway running: seed one customer with more than one page of mixed appointments, including two with an equal `StartUtc` and one starting exactly at "now"; then check `upcoming`, `past`, `status`, both together, page past the end, `pageSize=100`, the order and the paging stability across pages, and `EXPLAIN` showing `IX_Appointments_CustomerId_StartUtc`. The same pass checks, through the real model binder, what the unit tests cannot: `?status=` and `?page=` (empty values) give `400`, `?page=abc` gives the shared `{status, detail}` `400` and not a `ValidationProblemDetails`, `?customerId=<other>` changes nothing, a Staff and an Admin token give `403`, and `GET /api/appointments/{id}` still works. Requests for these go into the Booking `.http` file or the gateway's `.http` file, whichever the tasks choose. The gateway needs no test change _(Req 4.4)_; `Gateway.Tests` already asserts the `appointments` catch-all route.

## Requirements traceability

| Requirement | Where |
|---|---|
| 1.1 | `GetMine` returns `Ok(PagedResponse)`; `PagedResponse<T>` |
| 1.2 | `sub`-only caller; no `customerId` parameter; repository always filters on `CustomerId` |
| 1.3 | `TryGetCaller` / `Unauthorised()` |
| 1.4 | empty `Items` with `TotalCount` 0 is a success; no `404` |
| 1.5, 1.6 | `AppointmentResponse.From`; no status filter unless `status` is given |
| 1.7 | the handler has no client and the repository only reads |
| 2.1–2.3 | `TryParseStatus`/`TryParseWhen`; repository `Where`s; one `TimeProvider` read |
| 2.4 | string binding, `ConvertEmptyStringToNull = false`, validator, `Error(400)` |
| 2.5 | no status inference; nothing is written |
| 3.1, 3.2 | the two `OrderBy` shapes, `ThenBy(Id)` |
| 3.3, 3.4 | `TryParsePage`/`TryParsePageSize`, no clamping |
| 3.5 | `skip >= total` early return |
| 3.6 | `Skip`/`Take` plus `CountAsync` |
| 3.7 | all validator rules evaluated, one joined `400` |
| 4.1, 4.2 | `CustomerPolicy`, authentication middleware |
| 4.3 | literal `my` outranks `{id:guid}`; reflection test |
| 4.4 | no gateway change |
| 4.5 | `MapOpenApi()` plus the XML summary |

## Open questions — resolved

The project owner approved the design and settled the four questions as follows. They are final.


1. **`PagedResponse<T>` in Booking, not `BuildingBlocks`.** Chosen to keep the shared library untouched until `FR-BKG-004` needs the same envelope. If you would rather create it in `BuildingBlocks/Models` now (it needs no ASP.NET dependency), only its namespace changes.
2. **Empty-value binding relies on `DisplayFormat(ConvertEmptyStringToNull = false)`.** That attribute is honoured by MVC's metadata provider for simple types, but the unit tests cannot prove it. The manual check covers it, and if it fails, the fallback is to read the four values from `Request.Query` in the action (`TryGetValue` distinguishes absent from empty). Say if you prefer that fallback from the start; it costs the generated OpenAPI parameter list.
3. **The OpenAPI schema shows `page` and `pageSize` as strings**, a visible side effect of decision 2. The alternative, `int?` parameters, would let a non-integer produce the framework's own `ValidationProblemDetails` and break Requirement 3.4 and 3.7. Accepted as written unless you want both (for example a custom `IModelBinder`, rejected here as more machinery than the feature needs).
4. **Messages are joined with `","`** with no space, copying the existing handlers so all `400`s read alike, even though a space would read better. Say if you want it changed for this endpoint only.

**Resolutions**

1. `PagedResponse<T>` stays in `Booking.Application/Models`; it moves to `BuildingBlocks` only when a second consumer exists.
2. The action uses `DisplayFormat(ConvertEmptyStringToNull = false)`, verified by the manual check in the last task. There is no `Request.Query` fallback up front; it is used only if that check fails.
3. `page` and `pageSize` appear as strings in the OpenAPI document. Accepted.
4. The `","` join of validation messages is kept, so every `400` in the service reads alike.

**Related change in `shared-web-infrastructure` (Requirement 4.5).** Model-binding failures elsewhere become `{status: 400, detail: "Invalid request data. Errors: ..."}` `application/problem+json` through a shared `InvalidModelStateResponseFactory`. This endpoint's string-binding approach stays: it cannot fail binding, so its single `400` listing every failed rule still comes from the validator, and it needs nothing from that factory.
