# Shared web infrastructure — Design

> References like _(Req 1.2)_ point at [`requirements.md`](requirements.md).

## Overview

The shared code goes into a **new** project, `src/Shared/SmartAppointments.BuildingBlocks.Web`, rather than into `SmartAppointments.BuildingBlocks`. The existing library is referenced by every `*.Domain` project, and the web code needs the ASP.NET Core shared framework. Putting it in the existing library would make every domain project depend on ASP.NET Core, which Clean Architecture forbids. Two libraries keep the split: `BuildingBlocks` for plain types (`Result<T>`, `Error`, `Constants`), and `BuildingBlocks.Web` for anything that touches HTTP.

## Components

| File | Contents | Req |
|---|---|---|
| `Authentication/JwtValidationOptions.cs` | `Issuer`, `Audience`, `SecretKey`. Auth keeps its own `JwtOptions`, which adds token lifetimes for `TokenGenerator`, and binds the same `Jwt` section. | 1.1 |
| `Authentication/AuthenticationExtensions.cs` | `AddJwtAuthentication(this IServiceCollection, IConfiguration)` and `AddAuthorizationWithRoles(this IServiceCollection)` | 1.1–1.3 |
| `OpenApi/OpenApiExtensions.cs` | `AddOpenApiWithBearerAuth(this IServiceCollection)` | 1.4 |
| `Middlewares/ExceptionMiddleware.cs` | Exception → `ApiProblemDetails`, with the Req 3 mapping | 2.2, 3 |
| `Middlewares/LoggingMiddleware.cs` | Unchanged from today's copies | 2.3 |
| `Results/ErrorResultExtensions.cs` | `ToActionResult(this ControllerBase, Error)` | 2.1 |

`AddJwtAuthentication` is renamed from `AddAuthentication` so that it is not confused with the framework's own `AddAuthentication` overloads.

The project uses `<FrameworkReference Include="Microsoft.AspNetCore.App" />`, plus the `Microsoft.AspNetCore.Authentication.JwtBearer` and `Microsoft.AspNetCore.OpenApi` packages, which are not part of the shared framework. It references `SmartAppointments.BuildingBlocks` for `Constants`, `Error` and `ApiProblemDetails`.

### Composition

Each `*.Api` project references `BuildingBlocks.Web`, and `Program.cs` becomes:

```csharp
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddOpenApiWithBearerAuth();
builder.Services.AddJwtAuthentication(builder.Configuration);
builder.Services.AddAuthorizationWithRoles();
```

The pipeline order is unchanged. `AddApplication()` no longer registers OpenAPI, and the `*.Application` projects drop the `JwtBearer` and `OpenApi` packages they only needed for the moved code. This moves auth wiring out of the Application layer, where `CLAUDE.md` currently says it lives, and `CLAUDE.md` is updated to match.

### Exception mapping

| Exception | Status | Body `Detail` |
|---|---|---|
| `UnauthorizedAccessException` | 401 | "Unauthorized access." |
| `BadHttpRequestException` | its `StatusCode` | "The request could not be read." |
| anything else, including `InvalidOperationException` and `ArgumentException` | 500 | "An unexpected error occurred! Please try again later." |

No branch of the mapping echoes `ex.Message` (Req 3.1). The previous `ArgumentNullException`/`InvalidOperationException` → 400 rule had no legitimate caller: validation failures are returned as `Result` errors, and model-binding failures are answered by `[ApiController]` before the action runs.

## Testing strategy

A new `tests/BuildingBlocks.Tests` project (xUnit + Moq, like the others) takes over the two identical `ExceptionMiddlewareTests` classes and adds:

| Test class | Covers |
|---|---|
| `ExceptionMiddlewareTests` | The mapping table above; no message is echoed for any status; pass-through when nothing throws |
| `AuthenticationExtensionsTests` | Each fail-fast rule (Req 1.2); the validation parameters (Req 1.1); each policy's role set, read back through `IAuthorizationPolicyProvider` (Req 1.3) |
| `ErrorResultExtensionsTests` | Each status maps to its `ObjectResult` type and code (Req 2.1) |

The existing controller tests in both services keep running unchanged and confirm that the controllers still map errors the same way.

## Requirement 4: one error body

> Added after the original four tasks shipped. References like _(Req 4.1)_ point at Requirement 4.

### Changes

| File | Change | Req |
|---|---|---|
| `BuildingBlocks.Web/Results/ProblemDetailsWriter.cs` | Serialize with `JsonSerializerOptions.Web`, so the body becomes `{"status":..,"detail":".."}` | 4.2 |
| `BuildingBlocks.Web/Results/ErrorResultExtensions.cs` | Keep the status switch. Each arm returns an `ObjectResult` whose value is `new ApiProblemDetails(error.Status, error.Details)`, with `ContentTypes = { "application/problem+json" }`. MVC's output formatter writes it camelCase, like every other response. | 4.1 |
| `Booking.Infrastructure/Clients/AvailabilityClient.cs` (~line 124) | Read Availability's error body as `ApiProblemDetails` and use `Detail`. `ReadFromJsonAsync` uses web defaults, so it matches the camelCase body case-insensitively. Without this change, Booking would replace Availability's specific 409 message ("Slot is full.") with its generic fallback. | 4.1 (consumer) |

The body's `status` is the status the response actually has. It differs from `Error.Status` only for an unmapped value such as 418, which goes out as a 500 with `"status":500`, so the body and the HTTP status never disagree.

`Error` stays exactly as it is, because it is the domain type that handlers return. Only its HTTP representation changes.

The record is still named `ApiProblemDetails`, and the body is still not full RFC 9457 (no `type` or `title`). Adding those fields would mean more for the frontend to ignore and gains nothing here.

### Idempotency replay (Req 4.3)

Booking stores a failed outcome by serializing the `Error` record (`{"status":..,"details":..}`, `StoredResponseJson`). On replay it deserializes the stored value back into an `Error` and returns it through `ToActionResult`. The stored format is internal, so it does not change. Old and new records replay the same way and come out in the new HTTP shape, with no migration.

### Model-binding errors (Req 4.5)

`BuildingBlocks.Web/Results/ModelBindingProblemExtensions.cs` adds `AddSharedProblemDetails(this IMvcBuilder)`, which sets `ApiBehaviorOptions.InvalidModelStateResponseFactory`. The factory collects the `ModelState` error messages (skipping empty ones and duplicates) and returns a `400` `ObjectResult` carrying `ApiProblemDetails(400, "Invalid request data. Errors: " + messages joined by ",")` with `application/problem+json`. An error that has an exception but no message contributes the fixed text "The request body could not be read."; exception text is never echoed. With no usable message at all, the detail is that fixed text alone.

Each `*.Api` `Program.cs` chains it onto the existing call: `builder.Services.AddControllers().AddSharedProblemDetails()` (before any `.AddJsonOptions(...)`), so the composition order in CLAUDE.md does not change. The gateway has no MVC and is not touched. Tests: `ModelBindingProblemExtensionsTests` calls the factory with a populated `ModelState`.

### Order of rollout

Availability and Booking ship from one solution and one Compose stack, so they switch together. A Booking built from an older revision that talks to a new Availability would still work, only with the generic message. Nothing else consumes these bodies.

### Testing

- `ErrorResultExtensionsTests`: each status still maps to its code. The value is an `ApiProblemDetails` carrying the `Error`'s status and details, and `ContentTypes` contains `application/problem+json`.
- `ProblemDetailsWriterTests`: the exact body becomes `{"status":429,"detail":"Too many requests."}`.
- `RateLimitRejectionTests` and `ProxyErrorMiddlewareTests` (Gateway), and `ExceptionMiddlewareTests`: deserialize with `JsonSerializerOptions.Web`.
- `AvailabilityClientTests`: the fake Availability 409 response uses the new camelCase body, and a new test checks that its `detail` reaches the `Error`.
- Handler tests that assert `result.Error.Details` are unaffected, because `Error` did not change.
