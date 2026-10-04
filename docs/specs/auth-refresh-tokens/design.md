# Auth refresh tokens — Design

References like _(Req 2.3)_ point at requirement 2, criterion 3 in [`requirements.md`](requirements.md). Error bodies are `{"status":<int>,"detail":"..."}` through the shared `this.ToActionResult(error)` ([`shared-web-infrastructure` Requirement 4](../shared-web-infrastructure/requirements.md)); this design adds no error-shape code of its own.

## Overview

A refresh token is an opaque 64-byte random secret. The server keeps only its SHA-256 hash, in a `RefreshTokens` row that belongs to a **family**: the chain of tokens that starts at one login and grows by one row per refresh. Redeeming a token revokes it and inserts its successor in the same family; presenting a token that is already revoked revokes the whole family. Nothing is ever deleted by this feature.

The decisions that are not obvious:

- **Rotation is one conditional `UPDATE`, not a read followed by a write.** `UPDATE "RefreshTokens" SET "RevokedAtUtc" = @now, "ReplacedById" = @new WHERE "Id" = @id AND "RevokedAtUtc" IS NULL`, through `ExecuteUpdateAsync`, in the same transaction as the successor insert (the idiom of Booking's `IdempotencyRepository` and Availability's slot reserve). One row affected means this request won; zero means the token was already revoked, which is a reuse. Under PostgreSQL's default READ COMMITTED a second request on the same row blocks on the winner's row lock until the winner commits, re-evaluates `RevokedAtUtc IS NULL`, and gets zero rows. So the loser always runs its family revoke **after** the winner's successor is committed, and revokes it too _(Req 2.2, 2.7, 3.3)_.
- **The update runs before the insert.** `ReplacedById` is a plain nullable column with no foreign key (it is bookkeeping, not an integrity rule, and a later purge of old rows should not trip over it), so the update can go first. A loser then writes nothing at all, and a failed insert rolls the update back, leaving the presented token unredeemed _(Req 2.2)_.
- **The family's lifetime rules live in the Domain.** `RefreshToken.StartFamily` and `CreateSuccessor` are pure functions of the clock and two `TimeSpan`s, so the sliding-under-a-cap arithmetic _(Req 2.3, 2.4)_ is unit-tested without a mock.
- **The family start is stored, the cap is not.** Rows carry `FamilyStartedAtUtc`; the cap is computed as `FamilyStartedAtUtc + Jwt:RefreshTokenFamilyMaxDays` at each refresh, which is exactly the wording of Requirement 2.3 and lets a lowered setting take effect on live families. (With a stored cap, the "family already at its cap" case of Requirement 2.4 would be unreachable, since a token's own expiry never exceeds the cap; with a computed one it is reachable only after the setting is lowered, and is handled.)
- **`accessTokenExpiresAtUtc` is the `exp` claim by construction.** `TokenGenerator` takes "now" from `TimeProvider`, truncates the expiry to whole seconds (a JWT `exp` is whole seconds), puts that exact value in the descriptor and returns it beside the token. It is not recomputed by the handler _(Req 1.2)_.
- **Revocation by the reuse path is not in the handler's transaction.** After `TryRotateAsync` returns false the handler calls `RevokeFamilyAsync`, a separate idempotent statement. Doing it in the rotation transaction would only add lock time.
- **Expiry is not leak evidence.** The handler checks "revoked" before "expired", so a revoked-and-expired token still revokes its family (harmless), while an expired-only token does not _(Req 3.6)_.

This feature **supersedes `auth-identity` Requirements 6 and 7 and its tasks 15-18** (a chain model, descendants-only revocation on replay, single-token logout, purge-on-login, no absolute lifetime). The tasks written from this design will mark those requirements and tasks as superseded with a pointer here, and add a row for this feature to `docs/specs/README.md`. One caution is recorded in Open questions: task 15 (the design-time DbContext factory) is not about refresh tokens and is still needed.

## Architecture

| Layer | Contents |
|---|---|
| `Auth.Api` | Two new actions on `AuthController`: `Refresh` and `Logout`, both anonymous. `ProducesResponseType` attributes for the OpenAPI document _(Req 5.7)_. `appsettings.json` gains `Jwt:RefreshTokenFamilyMaxDays`. `Auth.Api.http` gains sample requests. |
| `Auth.Application` | `RefreshTokenCommand`, `LogoutCommand` and their handlers and validators; changes to `LoginUserHandler`; `RefreshTokenRequest`, `LogoutRequest`, `AccessToken`; `TokenResponse` gains `AccessTokenExpiresAtUtc`; `JwtOptions` gains `RefreshTokenFamilyMaxDays` and two lifetime accessors; abstractions `IRefreshTokenRepository`, `IRefreshTokenHasher`; `ITokenGenerator` changes signature; `IUserRepository` gains `GetByIdAsync`; `AddApplication()` registers `TimeProvider.System` and the two validators. |
| `Auth.Domain` | `Entities/RefreshToken.cs`. |
| `Auth.Infrastructure` | `RefreshTokens` configuration in `ApplicationDbContext`; `Persistence/RefreshTokenRepository.cs`; `Services/RefreshTokenHasher.cs`; `TokenGenerator` takes `TimeProvider`; `UserRepository.GetByIdAsync` and a tightened unique-violation check; DI registrations and options validation; a design-time DbContext factory; migration `AddRefreshTokens`. |
| `SmartAppointments.Gateway` | Routes `auth-refresh`, `auth-logout` in `appsettings.json`; `RateLimitPolicies.Refresh` and a parametrized `ByClientIp`. `SmartAppointments.Gateway.http` gains samples. |

Dependency direction is unchanged: Api -> Application -> Domain, Infrastructure -> Application. `Auth.Application` has no EF Core reference, so the transaction lives inside `RefreshTokenRepository`, and `IRefreshTokenRepository.TryRotateAsync` is the Application-level name for "rotate atomically".

## Components and interfaces

### Endpoints

| Verb / route | Action | Authorization | Dispatches | Success | Failures |
|---|---|---|---|---|---|
| `POST /api/auth/login` (changed) | `Login` | anonymous | `LoginUserCommand` | `200` `TokenResponse` (now with `accessTokenExpiresAtUtc`) | unchanged |
| `POST /api/auth/refresh` | `Refresh(RefreshTokenRequest, ct)` | anonymous (`[AllowAnonymous]`) | `RefreshTokenCommand(RefreshToken)` | `200` `TokenResponse` | `400`, `401` |
| `POST /api/auth/logout` | `Logout(LogoutRequest, ct)` | anonymous (`[AllowAnonymous]`) | `LogoutCommand(RefreshToken)` | `204` no body | `400` |

Both new actions are anonymous because the refresh token is the credential. An expired or malformed `Authorization` header does not reject them: `UseAuthentication` leaves the principal empty on a failed bearer and only a failed *authorization* produces `401`, and these actions have no policy _(Req 4.4, 5.6)_. `[AllowAnonymous]` is there so a later fallback policy cannot silently break this.

Action bodies follow `Login`: build the command, `await sender.Send(command, cancellationToken)`, `if (!result.IsSuccess) return this.ToActionResult(result.Error!);`, then `Ok(result.Value)` for refresh or `NoContent()` for logout.

```csharp
public sealed record TokenResponse(string AccessToken, string RefreshToken, DateTime AccessTokenExpiresAtUtc);
public sealed record RefreshTokenRequest(string? RefreshToken);
public sealed record LogoutRequest(string? RefreshToken);
public sealed record AccessToken(string Value, DateTime ExpiresAtUtc);   // from ITokenGenerator
```

`AccessTokenExpiresAtUtc` is a `DateTime` of kind `Utc` with whole seconds, which System.Text.Json writes as `2026-10-04T13:00:00Z` _(Req 1.2)_. The request records use `string?` on purpose: with nullable context on, a non-nullable `string` would make MVC's implicit `[Required]` reject a missing or empty value before the handler runs, with a body that is not ours. With `string?` the validator produces the "Invalid request: ..." message _(Req 3.5)_ and logout can treat empty as success _(Req 4.2, 4.6)_.

OpenAPI _(Req 5.7)_: `[ProducesResponseType<TokenResponse>(200)]`, `[ProducesResponseType<ApiProblemDetails>(400)]`, `[ProducesResponseType<ApiProblemDetails>(401)]` on `Refresh`; `[ProducesResponseType(204)]` and `400` on `Logout`; the `200` type on `Login` is added too, so `accessTokenExpiresAtUtc` appears for all three. Request bodies come from the parameters.

### Commands, queries and handlers

```csharp
public sealed record RefreshTokenCommand(string? RefreshToken) : IRequest<Result<TokenResponse>>;
public sealed record LogoutCommand(string? RefreshToken) : IRequest<Result<Unit>>;   // MediatR Unit
```

**`LoginUserHandler` (modified)** — new constructor dependencies: `IRefreshTokenRepository`, `IRefreshTokenHasher`, `IOptions<JwtOptions>`, `TimeProvider`. Everything up to the password check is unchanged _(Req 1.7: no token is persisted on any 401)_. Then:

1. `now = timeProvider.GetUtcNow().UtcDateTime`.
2. `access = tokenGenerator.GenerateAccessToken(user)`; `raw = tokenGenerator.GenerateRefreshToken()`; `hash = hasher.Hash(raw)`.
3. `family = RefreshToken.StartFamily(user.Id, hash, now, jwt.GetRefreshTokenLifetime(), jwt.GetRefreshTokenFamilyMaxLifetime())` _(Req 1.3, 1.6: a new `FamilyId` each login; other families are never read)_. The two accessors throw `InvalidOperationException` for a non-positive setting _(Req 1.8)_.
4. `user.RecordLogin()`; `await refreshTokens.AddAsync(family, ct)` (stages only); `await userRepository.SaveChangesAsync(ct)`. Both repositories share the scoped `ApplicationDbContext`, so the login timestamp and the new token commit atomically and there is no second commit.
5. Return `new TokenResponse(access.Value, raw, access.ExpiresAtUtc)`.

Steps 2-3 run before the save so a throw (bad configuration) writes nothing.

**`RefreshTokenCommandHandler`** — dependencies: `IRefreshTokenRepository`, `IUserRepository`, `ITokenGenerator`, `IRefreshTokenHasher`, `IValidator<RefreshTokenCommand>`, `IOptions<JwtOptions>`, `TimeProvider`, `ILogger<>`. Every failure below is `Error(401, "Invalid or expired refresh token.")` ("`Unauthorized`" in the list) _(Req 3.1)_:

1. Validate -> on failure `Error(400, $"Invalid request: {errors}")`, **before any repository call** _(Req 3.5)_.
2. If `RefreshToken.Length > 256` -> `Unauthorized`, without hashing _(Req 3.1)_. (A real token is 88 characters; the cap bounds the work an anonymous caller can force.)
3. `hash = hasher.Hash(token)`; `stored = await refreshTokens.GetByHashAsync(hash, ct)`. `null` -> `Unauthorized` (unknown or malformed; no family to log).
4. `stored.RevokedAtUtc is not null` -> `RevokeFamilyAsync(stored.FamilyId, now)`, log warning with the family id, `Unauthorized` _(Req 3.2, 3.3)_.
5. `stored.ExpiresAtUtc <= now` -> log warning, `Unauthorized`, **no revoke** _(Req 3.6)_.
6. `user = await userRepository.GetByIdAsync(stored.UserId, ct)`; `user is null || !user.IsActive` -> `RevokeFamilyAsync`, `Unauthorized` _(Req 3.1, 3.4)_.
7. `successorHash` from a fresh `GenerateRefreshToken()`; `successor = stored.CreateSuccessor(successorHash, now, lifetime, maxLifetime)`. `null` (family at its cap) -> `Unauthorized`, no revoke _(Req 2.4)_.
8. `access = tokenGenerator.GenerateAccessToken(user)` — built from the user as just read, so email and role are current _(Req 2.5)_. It runs before the rotation so that a generation failure leaves the token unredeemed.
9. `rotated = await refreshTokens.TryRotateAsync(stored.Id, successor, now, ct)`. `false` -> lost the race or reuse: `RevokeFamilyAsync(stored.FamilyId, now)`, log warning, `Unauthorized` _(Req 2.7, 3.2)_.
10. Return `TokenResponse(access.Value, rawSuccessor, access.ExpiresAtUtc)` _(Req 2.1, 2.3)_. `user.RecordLogin()` is **not** called and nothing is saved on the user _(Req 2.6)_; `GetByIdAsync` is a no-tracking read.

Logging: warnings carry the family id when known and a short reason; the token, its hash and the user's email are never logged _(Req 1.4, 3.7)_. The reason is for the server log only; the client sees the one message.

**`LogoutCommandHandler`** — dependencies: `IRefreshTokenRepository`, `IRefreshTokenHasher`, `IValidator<LogoutCommand>`, `TimeProvider`, `ILogger<>`.

1. Validate (`NotNull`) -> `Error(400, "Invalid request: Refresh token is required.")` _(Req 4.6)_.
2. `string.IsNullOrWhiteSpace(token)` or longer than 256 -> `Success(Unit.Value)` without touching the database _(Req 4.2)_.
3. `stored = GetByHashAsync(hash)`; `null` -> `Success`. Otherwise `RevokeFamilyAsync(stored.FamilyId, now)` — whether the presented token is current, rotated, expired or already revoked _(Req 4.1)_ — and `Success`. Other families of the user are not touched because the statement filters on `FamilyId` _(Req 4.3)_.

Logout never reveals whether the token existed _(Req 4.2)_ and issues no token. Access tokens already issued stay valid until they expire _(Req 4.5, accepted)_.

### Abstractions

```csharp
// Auth.Application/Abstractions/IRefreshTokenRepository.cs
public interface IRefreshTokenRepository
{
    /// Stages a new family. Committed by IUserRepository.SaveChangesAsync (same scoped DbContext),
    /// so a login's LastLoginAtUtc and its token are one commit.
    Task AddAsync(RefreshToken token, CancellationToken ct);

    /// Read for query purposes only (AsNoTracking). Null when no row has this hash.
    Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken ct);

    /// Revokes the current token and inserts its successor in one transaction. True when this call
    /// revoked it; false when it was already revoked, in which case nothing was written.
    Task<bool> TryRotateAsync(Guid currentTokenId, RefreshToken successor, DateTime nowUtc, CancellationToken ct);

    /// Revokes every not-yet-revoked token of the family. Idempotent; returns the rows changed.
    Task<int> RevokeFamilyAsync(Guid familyId, DateTime nowUtc, CancellationToken ct);
}

// Auth.Application/Abstractions/IRefreshTokenHasher.cs
public interface IRefreshTokenHasher { string Hash(string token); }

// ITokenGenerator (changed)
public interface ITokenGenerator
{
    AccessToken GenerateAccessToken(User user);   // was string
    string GenerateRefreshToken();
}

// IUserRepository (added)
Task<User?> GetByIdAsync(Guid id, CancellationToken ct);   // AsNoTracking
```

**`RefreshTokenHasher`** (`Auth.Infrastructure/Services`): SHA-256 of the token's UTF-8 bytes, lower-case hex, 64 characters. It hashes the string exactly as presented rather than decoding base64 first, so a non-canonical spelling of a token simply fails to match. It is deliberately not BCrypt: the hash is the lookup key, so it must be deterministic, and a 512-bit random secret has nothing to brute-force (the reasoning of `auth-identity` design). Registered scoped next to the other services.

**`RefreshTokenRepository`** (`Auth.Infrastructure/Persistence`), sharing `ApplicationDbContext`:

- `AddAsync`: `context.RefreshTokens.AddAsync(token)`; no save.
- `GetByHashAsync`: `AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash)`, served by the unique index.
- `TryRotateAsync`:
  ```csharp
  await using var tx = await context.Database.BeginTransactionAsync(ct);
  var rows = await context.RefreshTokens
      .Where(t => t.Id == currentTokenId && t.RevokedAtUtc == null)
      .ExecuteUpdateAsync(s => s
          .SetProperty(t => t.RevokedAtUtc, nowUtc)
          .SetProperty(t => t.ReplacedById, successor.Id), ct);
  if (rows != 1) { return false; }          // disposing the transaction rolls back; nothing was written
  context.RefreshTokens.Add(successor);
  await context.SaveChangesAsync(ct);
  context.Entry(successor).State = EntityState.Detached;
  await tx.CommitAsync(ct);
  return true;
  ```
  An exception from the insert (for example a hash collision on the unique index, which would be a generator failure) disposes the transaction uncommitted, so the presented token stays redeemable, and propagates to `ExceptionMiddleware` as a `500` _(Req 2.2)_. No EF retry execution strategy is configured; if one is ever added this block must move inside `CreateExecutionStrategy().ExecuteAsync`.
- `RevokeFamilyAsync`: `Where(t => t.FamilyId == familyId && t.RevokedAtUtc == null).ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAtUtc, nowUtc))`.

All writes after login bypass the change tracker, as the other repositories do, so the entity's setters stay private.

**Two small repairs that this feature forces** in `UserRepository`: add `GetByIdAsync`, and make `IsUniqueViolation` check `ConstraintName == "IX_Users_Email"`. Today any unique violation on the shared context becomes `DuplicateEmailException`; with `IX_RefreshTokens_TokenHash` in the same context, a token-hash collision committed by the login's `SaveChangesAsync` would be reported as "Email is already registered." (409). Booking's `UniqueViolation.IsOn(ex, indexName)` is the model.

**Token generation.** `GenerateRefreshToken()` stays `Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))`: 64 bytes, 88 characters with padding _(Req 1.3 specifies base64)_. Base64url would only matter if the token were put in a URL or a header; it travels only in a JSON string, where `+`, `/` and `=` need no escaping, and changing the encoding would contradict an approved criterion for no gain. The 256-character cap leaves room for any future encoding. `TokenGenerator` changes as follows:

```csharp
public class TokenGenerator(IOptions<JwtOptions> options, TimeProvider timeProvider) : ITokenGenerator
{
    public AccessToken GenerateAccessToken(User user)
    {
        ... existing guards ...
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var expires = now.AddMinutes(expirationMinutes);
        expires = new DateTime(expires.Ticks - expires.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc); // whole seconds, like exp
        ... descriptor with Expires = expires ...
        return new AccessToken(tokenHandler.WriteToken(tokenHandler.CreateToken(descriptor)), expires);
    }
}
```

`TimeProvider` is **not used anywhere in Auth today**; `AddApplication()` registers `services.AddSingleton(TimeProvider.System)` exactly as Availability and Booking do, and `TokenGenerator` (Infrastructure) receives it from there. `User.RecordLogin()` keeps `DateTime.UtcNow`; it is not part of this feature.

**Options.** `JwtOptions` gains `public int RefreshTokenFamilyMaxDays { get; set; } = 30;` and:

```csharp
public TimeSpan GetRefreshTokenLifetime()          // throws InvalidOperationException when RefreshTokenExpirationDays <= 0
public TimeSpan GetRefreshTokenFamilyMaxLifetime() // throws when RefreshTokenFamilyMaxDays <= 0
```

`appsettings.json` ships `"RefreshTokenFamilyMaxDays": 30` beside `RefreshTokenExpirationDays: 7`; it is not a secret, so no per-environment override is needed. The accessors satisfy _(Req 1.8)_ at the point of use. In addition `AddInfrastructure` replaces `services.Configure<JwtOptions>(...)` with `AddOptions<JwtOptions>().Bind(section).Validate(...).ValidateOnStart()` so a bad value fails at startup rather than on the first login: `RefreshTokenExpirationDays > 0`, `RefreshTokenFamilyMaxDays >= RefreshTokenExpirationDays` (a cap shorter than one sliding window makes the sliding lifetime meaningless). The validation covers only the refresh settings; the existing runtime guard on `AccessTokenExpirationMinutes` is left as it is.

### Validation

| Validator | Rule | Criterion |
|---|---|---|
| `RefreshTokenCommandValidator` (`AbstractValidator<RefreshTokenCommand>`) | `RefreshToken` `NotEmpty()` ("Refresh token is required.") — rejects null, empty and whitespace | 3.5 |
| `LogoutCommandValidator` | `RefreshToken` `NotNull()` ("Refresh token is required.") — empty and whitespace pass and end as `204` in the handler | 4.2, 4.6 |

There is deliberately no `MaximumLength` rule: an over-long token must be `401` for refresh _(Req 3.1)_ and `204` for logout _(Req 4.2)_, not `400`. Both are registered in `AddApplication()` as `AddScoped<IValidator<...>, ...>` like the existing ones.

An absent body, a body that is not a JSON object, or a wrong JSON type for the field is rejected by `[ApiController]` model binding with `400` before the action runs _(Req 4.6)_; see Open questions for its body shape.

## Data model

**`RefreshToken`** (`Auth.Domain/Entities`), private setters, private parameterless constructor for EF like `User`:

| Property | Type | Notes |
|---|---|---|
| `Id` | `Guid` | `Guid.CreateVersion7()` |
| `UserId` | `Guid` | owner |
| `FamilyId` | `Guid` | the login that began the family; unchanged by rotation _(Req 1.6)_ |
| `TokenHash` | `string` | 64-char lower-case hex; the raw token is never a property _(Req 1.4)_ |
| `CreatedAtUtc` | `DateTime` | issue time |
| `ExpiresAtUtc` | `DateTime` | issue time + 7 days, capped at the family cap _(Req 1.3, 2.3)_ |
| `FamilyStartedAtUtc` | `DateTime` | the login time; copied unchanged into every successor _(Req 2.3)_ |
| `RevokedAtUtc` | `DateTime?` | set by rotation, logout or a family revoke |
| `ReplacedById` | `Guid?` | the successor, set only by rotation; null for a token revoked by logout or reuse |

```csharp
public static RefreshToken StartFamily(Guid userId, string tokenHash, DateTime nowUtc,
                                       TimeSpan lifetime, TimeSpan familyMaxLifetime);
public RefreshToken? CreateSuccessor(string tokenHash, DateTime nowUtc,
                                     TimeSpan lifetime, TimeSpan familyMaxLifetime); // same FamilyId and FamilyStartedAtUtc
public bool IsRevoked => RevokedAtUtc is not null;
public bool IsExpired(DateTime nowUtc) => ExpiresAtUtc <= nowUtc;
```

Both factories compute `expires = Min(nowUtc + lifetime, FamilyStartedAtUtc + familyMaxLifetime)`. `StartFamily` always returns a token (the start time is `nowUtc`, and the setting is validated `>= lifetime`; a guard still throws `ArgumentOutOfRangeException` for a non-positive `TimeSpan`). `CreateSuccessor` returns `null` when `expires <= nowUtc` _(Req 2.4)_. There is no `Revoke` method: revocation is a conditional statement in the repository, because a tracked entity changed in memory would reintroduce the read-then-write race the design removes.

**EF configuration** in `ApplicationDbContext` (`public DbSet<RefreshToken> RefreshTokens`):

```csharp
modelBuilder.Entity<RefreshToken>(entity =>
{
    entity.ToTable("RefreshTokens");
    entity.HasKey(e => e.Id);
    entity.Property(e => e.TokenHash).IsRequired().HasMaxLength(64);
    entity.Property(e => e.CreatedAtUtc).IsRequired();
    entity.Property(e => e.ExpiresAtUtc).IsRequired();
    entity.Property(e => e.FamilyStartedAtUtc).IsRequired();
    entity.HasIndex(e => e.TokenHash).IsUnique();   // Req 1.5; the lookup path
    entity.HasIndex(e => e.FamilyId);                // family revoke
    entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).IsRequired().OnDelete(DeleteBehavior.Cascade);
});
```

`DateTime` maps to `timestamptz` under Npgsql 10, as for `Users`; all values written are `DateTimeKind.Utc`. EF's convention adds `IX_RefreshTokens_UserId` for the foreign key. `ReplacedById` has no foreign key (see Overview). Nothing is added for `UserId` on `User` (no navigation).

**Migration `AddRefreshTokens`**, generated with `dotnet ef migrations add AddRefreshTokens --project src/Services/Auth/Auth.Infrastructure --startup-project src/Services/Auth/Auth.Api`, producing: `RefreshTokens` with `Id uuid`, `UserId uuid`, `FamilyId uuid`, `TokenHash character varying(64)`, `CreatedAtUtc`/`ExpiresAtUtc`/`FamilyStartedAtUtc timestamp with time zone not null`, `RevokedAtUtc timestamp with time zone null`, `ReplacedById uuid null`, the primary key, `IX_RefreshTokens_TokenHash` (unique), `IX_RefreshTokens_FamilyId`, `IX_RefreshTokens_UserId`, and `FK_RefreshTokens_Users_UserId` (cascade). The migration is reviewed by hand against this list. It is applied by `DatabaseMigrator` when `Database:MigrateOnStartup` is true (Compose) or by `dotnet ef database update`. A **design-time DbContext factory** (`IDesignTimeDbContextFactory<ApplicationDbContext>`, copied from the Availability/Booking one) is added in the same task so the command above works without a configured connection string.

## Error handling

Expected failures use `Result<T>`; unexpected ones fall through to `ExceptionMiddleware`.

| Condition | `Error.Status` | Controller result |
|---|---|---|
| Refresh: `refreshToken` missing, null, empty, whitespace (3.5) | 400 `Invalid request: Refresh token is required.` | `ToActionResult` -> `400` |
| Refresh: unknown, malformed, over 256 chars, expired, user inactive or gone, family at its cap, already revoked, lost the race (2.4, 2.7, 3.1, 3.2, 3.4, 3.6) | 401 `Invalid or expired refresh token.` | `ToActionResult` -> `401` |
| Refresh: success (2.1) | - | `Ok(TokenResponse)` |
| Logout: field missing or null (4.6) | 400 `Invalid request: Refresh token is required.` | `ToActionResult` -> `400` |
| Logout: body absent, not a JSON object, wrong type (4.6) | - | `[ApiController]` model binding `400` |
| Logout: unknown, empty, whitespace, over-long, already revoked, success (4.1, 4.2) | - | `NoContent()` |
| Login: unchanged failures (1.7) | 400 / 401 | unchanged |
| Misconfigured lifetime (1.8); token-hash collision or database failure (2.2) | exception | `ExceptionMiddleware` -> `500`, nothing committed |

A refresh `401` never says why _(Req 3.1)_. Rate limiting is the gateway's `429` with `Retry-After: 60` _(Req 5.3)_.

## Gateway

**Routes** added to `ReverseProxy:Routes` in `appsettings.json` (shared with the Development file by configuration layering; nothing changes in `appsettings.Development.json`):

```json
"auth-refresh": {
  "ClusterId": "auth",
  "Order": 0,
  "AuthorizationPolicy": "anonymous",
  "RateLimiterPolicy": "refresh",
  "Match": { "Path": "/api/auth/refresh", "Methods": [ "POST" ] },
  "Transforms": [ { "RequestHeaderOriginalHost": "true" } ]
},
"auth-logout": {
  "ClusterId": "auth",
  "Order": 0,
  "AuthorizationPolicy": "anonymous",
  "Match": { "Path": "/api/auth/logout", "Methods": [ "POST" ] },
  "Transforms": [ { "RequestHeaderOriginalHost": "true" } ]
}
```

(_Req 5.1, 5.4._) With `Methods: [ "POST" ]`, other verbs fall through to the existing `auth` catch-all at `Order` 10, whose default policy answers `401` without a token _(Req 5.2)_. The shipped config goes from nine routes to eleven.

**Rate limit.** `RateLimitPolicies` gains `public const string Refresh = "refresh";` and `public const int RefreshPermits = 10;`. `ByClientIp` is parametrized, keeping the old signature so nothing else changes:

```csharp
public static RateLimitPartition<string> ByClientIp(HttpContext context) => ByClientIp(context, LoginPermits);

public static RateLimitPartition<string> ByClientIp(HttpContext context, int permits)
    => RateLimitPartition.GetSlidingWindowLimiter(IpKey(context), _ => Options(permits));
```

`AddGatewayRateLimiting` adds `options.AddPolicy(Refresh, context => ByClientIp(context, RefreshPermits));`. Window, segments, no queue, remote-address partition and ignored forwarded headers are inherited from the shared helpers _(Req 5.3)_. Counters are independent of `login`'s because `AddPolicy` builds one limiter per policy name, so the same address key in two policies is two separate counters. The existing `RejectAsync` already writes `Retry-After` 60 and the `429` body. Logout has no `RateLimiterPolicy` _(Req 5.4)_.

**`.http` samples.** `SmartAppointments.Gateway.http` (anonymous section) gains, after the login request, `POST /api/auth/refresh` with `{ "refreshToken": "<paste refreshToken from login>" }` and `POST /api/auth/logout` with the same body, with a comment that the 11th refresh in a minute is `429` and that a replayed refresh token is `401` and kills the family. `Auth.Api.http` currently holds only the template `weatherforecast` request; it is replaced with login, refresh and logout requests against `{{Auth.Api_HostAddress}}` (the service directly, `http://localhost:5008`).

## Testing strategy

Pure unit tests in the existing style (xUnit + Moq, no FluentAssertions); `Auth.Tests` gets its own `FixedTimeProvider` (the other test projects each carry an `internal` copy). Existing `LoginUserHandlerTests` and `TokenGeneratorTests` are updated for the new constructor parameters and the `AccessToken` return type.

| Test class | Covers |
|---|---|
| `RefreshTokenTests` (Domain) | `StartFamily` expiry = now + 7 d, family id and start set (1.3, 1.6); `CreateSuccessor` keeps `FamilyId` and `FamilyStartedAtUtc` (2.3); expiry is `min(sliding, cap)` for a young and for an old family; returns `null` at or past the cap (2.4); `IsExpired` boundary; no property holds the raw token (1.4) |
| `LoginUserHandlerTests` (updated) | success persists a family via `AddAsync` and commits once with the user save (1.3, 1.6); response carries `AccessTokenExpiresAtUtc` equal to the generator's (1.1, 1.2); every 401 path never calls `AddAsync` (1.7); zero or negative `RefreshTokenExpirationDays` throws and nothing is saved (1.8) |
| `RefreshTokenCommandHandlerTests` | happy path: returns a new pair, rotation called with the stored id and a successor in the same family, email and role come from the user read at refresh time, `RecordLogin` not called (2.1, 2.3, 2.5, 2.6); unknown / over-256 (hasher and repository **not** called) / expired (no revoke) / revoked (family revoked, no token issued) / inactive user (family revoked) / user gone / family at cap (2.4, 3.1, 3.2, 3.4, 3.6); `TryRotateAsync` false -> family revoked and `401` (2.7, 3.2); invalid request -> `400` with no repository call (3.5); the access token is generated before the rotation call (ordering, via `MockSequence` or callbacks); log output never contains the token or hash (3.7, via a capturing logger) |
| `LogoutCommandHandlerTests` | known token (current, rotated, expired, already revoked) -> family revoked once, `Success`; unknown -> `Success`, no revoke; empty, whitespace, over-long -> `Success`, no repository call; `null` -> `400` (4.1, 4.2, 4.6) |
| `RefreshTokenCommandValidatorTests`, `LogoutCommandValidatorTests` | null, empty, whitespace fail the first; only null fails the second (3.5, 4.6) |
| `RefreshTokenHasherTests` | deterministic; 64 lower-case hex characters; output does not contain the input; different inputs differ |
| `TokenGeneratorTests` (updated) | `ExpiresAtUtc` equals the JWT's `exp` claim (as seconds) and `ValidTo` exactly, with a fixed clock (1.2); expiry is whole seconds; refresh token decodes to 64 bytes and two calls differ (1.3) |
| `JwtOptionsTests` | accessors return the configured spans and throw on `<= 0`; default `RefreshTokenFamilyMaxDays` is 30; `ValidateOnStart` predicate rejects a cap below the sliding window (1.8) |
| `AuthControllerTests` (extended) | `Refresh` -> `OkObjectResult` carrying the `TokenResponse`; a failed `Result` -> the status through `ToActionResult`; `Logout` -> `NoContentResult`, `400` on a failed result; the routes and `[AllowAnonymous]` (reflection) (5.6) |
| `UserRepository` unique-violation check | not unit-tested (needs a database); covered by the manual run below |
| `Gateway.Tests` | `RoutingConfigurationTests`: eleven routes; `auth-refresh` and `auth-logout` are `POST`, `Order` 0, policy `anonymous`, cluster `auth`; `RateLimiterPolicy` is `refresh` on `auth-refresh` only and absent on `auth-logout`; the other nine routes and the three existing limiters are unchanged (5.1, 5.2, 5.4, 5.5). `RateLimitPoliciesTests`: `Refresh == "refresh"`, `RefreshPermits == 10`; `ByClientIp(context, RefreshPermits)` permits exactly 10 then rejects, keys on the remote address and ignores `X-Forwarded-For`; `ByClientIp(context)` still permits 5 (5.3) |

**What is not covered by an automated test.** `RefreshTokenRepository`'s SQL, the transaction, the unique and foreign-key constraints and the migration need a real PostgreSQL, and the solution has no Testcontainers or database-backed test harness (the same position as Booking's and Availability's repositories). Concurrency _(Req 2.7, 3.3)_ is therefore covered by design (the single conditional statement and READ COMMITTED row-lock semantics above) and verified by hand against local PostgreSQL in the last task:

1. Apply the migration (`dotnet ef database update`) and compare the generated schema, including both indexes and the cascade, with the Data model section; `dotnet ef migrations script` reviewed.
2. Through the running service, log in, then fire the same refresh token twice in parallel (for example `1..2 | ForEach-Object -Parallel { Invoke-RestMethod ... }` in PowerShell, repeated 20 times with fresh logins): exactly one `200` and one `401` each time; afterwards the winner's new token is also `401` (family revoked) and a second login's family is unaffected (2.7, 3.3, 4.3).
3. Replay a rotated token (`401`, family dead), refresh after the expiry is moved into the past in the database (`401`, family not revoked), log out then refresh (`401`), log out twice (`204` both), deactivate a user in the database then refresh (`401`, family revoked, still `401` after reactivating) (2.4, 3.2, 3.4, 3.6, 4.1, 4.2).
4. Through the gateway: the 11th refresh from one address in a minute is `429` with `Retry-After: 60`, and login permits are unaffected; `GET /api/auth/refresh` is `401`; refresh and logout work with no `Authorization` header and with an expired one (4.4, 5.2, 5.3).
5. `dotnet build` and `dotnet test SmartAppointments.slnx` green; open `/scalar` and confirm both endpoints and `accessTokenExpiresAtUtc` appear (5.7).

## Requirements traceability

| Req | Satisfied by |
|---|---|
| 1.1, 1.2 | `TokenResponse.AccessTokenExpiresAtUtc`; `AccessToken` returned by `TokenGenerator` with whole-second expiry from `TimeProvider` |
| 1.3 | `RefreshToken.StartFamily`; `GenerateRefreshToken` (64 bytes, base64); `LoginUserHandler` steps 2-4 |
| 1.4 | only `TokenHash` is a property; hasher; logging rules; no raw value in any log call |
| 1.5 | unique index `IX_RefreshTokens_TokenHash` |
| 1.6 | new `FamilyId` per `StartFamily`; no read of other families |
| 1.7 | login failures return before step 2 |
| 1.8 | `JwtOptions` accessors throw; `ValidateOnStart` |
| 2.1, 2.5, 2.6 | refresh handler steps 6, 8, 10; `GetByIdAsync` no-tracking |
| 2.2 | `TryRotateAsync` single transaction, update before insert, rollback on failure |
| 2.3, 2.4 | `CreateSuccessor` (`min(sliding, cap)`, `null` at the cap), `FamilyStartedAtUtc`, handler step 7 |
| 2.7 | conditional `ExecuteUpdate` with `RevokedAtUtc IS NULL`; loser takes the reuse path |
| 3.1 | one error message; handler steps 2-3, 5-7; 256 cap before hashing |
| 3.2, 3.3 | handler step 4 and 9; `RevokeFamilyAsync` runs after the winner's commit |
| 3.4 | handler step 6 |
| 3.5 | `RefreshTokenCommandValidator`, step 1 before repository access |
| 3.6 | step 5 (no revoke); revoked is checked before expired |
| 3.7 | warning logs with family id only |
| 4.1, 4.2, 4.3 | `LogoutCommandHandler`; revoke filters on `FamilyId` |
| 4.4 | no `[Authorize]`, `[AllowAnonymous]`; gateway routes anonymous |
| 4.5 | accepted; documented, nothing built |
| 4.6 | `LogoutCommandValidator` (`NotNull`) plus model binding for body-level errors |
| 5.1, 5.2, 5.4 | the two route entries |
| 5.3 | `RateLimitPolicies.Refresh`, parametrized `ByClientIp` |
| 5.5 | `Gateway.Tests` updates |
| 5.6 | controller actions with no policy; controller tests |
| 5.7 | `ProducesResponseType` attributes; typed request and response records |

## Open questions

1. **`auth-identity` task 15 is not about refresh tokens.** The brief says tasks 15-18 are superseded, but task 15 is the `IDesignTimeDbContextFactory` for Auth, which does not exist (Auth has none; Availability and Booking do) and without which `dotnet ef migrations add AddRefreshTokens` cannot run against the blank `ConnectionStrings:DefaultConnection`. This design carries that factory into this feature's tasks and the tasks should mark `auth-identity` task 15 as "moved here" rather than "superseded". Is that right?
2. **Body-level `400`s (absent body, not a JSON object, wrong field type) are produced by MVC's `[ApiController]` model binding**, whose default body is a `ValidationProblemDetails` (`errors` dictionary), not `{status, detail}`. `shared-web-infrastructure` Requirement 4 covers `Result` errors and middleware errors but not this path (the same already applies to login and register). Options: accept it for this feature, or extend the shared library with an `InvalidModelStateResponseFactory` that writes `{status, detail}` (a change to `BuildingBlocks.Web`, outside this spec). Which?
3. **Hash-collision and unique-violation handling in `UserRepository`** is tightened here (constraint-name check) as a side effect; confirm that touching `UserRepository` for this is acceptable rather than a separate change.
4. **`ValidateOnStart` for the refresh settings** makes a misconfigured value a startup failure, beyond the "throw rather than issue" of Requirement 1.8; the accessors alone would satisfy the requirement. Keep both?

## Open questions — resolved

The design was approved with these resolutions:

1. **`auth-identity` task 15 is moved, not superseded.** The design-time DbContext factory is built here (task 2 of [`tasks.md`](tasks.md)); `auth-identity` marks task 15 "moved to auth-refresh-tokens", while its Requirements 6 and 7 and tasks 16-18 are "superseded by auth-refresh-tokens".
2. **Body-level `400`s** (absent body, not a JSON object, wrong type) are resolved separately by `shared-web-infrastructure` Requirement 4.5: a shared `InvalidModelStateResponseFactory` makes them `{"status":400,"detail":"Invalid request data. Errors: ..."}` as `application/problem+json` in every service. Nothing is built here beyond expecting that shape in the Error handling table and in tests.
3. **The `UserRepository` unique-violation fix is in scope** (constraint-name check on `IX_Users_Email`, task 4).
4. **`ValidateOnStart` is kept** for the refresh settings, in addition to the throwing accessors.
