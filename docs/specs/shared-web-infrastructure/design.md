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
