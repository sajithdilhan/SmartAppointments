# Auth refresh tokens — Tasks

Implementation branches from `unified-error-shape`, so errors are the camelCase `{"status","detail"}` body from [`shared-web-infrastructure` Requirement 4](../shared-web-infrastructure/requirements.md). Each task leaves `dotnet build SmartAppointments.slnx` and `dotnet test SmartAppointments.slnx` green. References are to [`requirements.md`](requirements.md) and [`design.md`](design.md).

- [x] 1. Shared pieces: options, clock, access-token result
  - `JwtOptions.RefreshTokenFamilyMaxDays` (default 30) and `GetRefreshTokenLifetime()` / `GetRefreshTokenFamilyMaxLifetime()` throwing `InvalidOperationException` for a value <= 0; `"RefreshTokenFamilyMaxDays": 30` in Auth's `appsettings.json`
  - `AddInfrastructure`: `AddOptions<JwtOptions>().Bind(...).Validate(...).ValidateOnStart()` — `RefreshTokenExpirationDays > 0` and `RefreshTokenFamilyMaxDays >= RefreshTokenExpirationDays`
  - `AddApplication()` registers `services.AddSingleton(TimeProvider.System)`
  - `AccessToken(Value, ExpiresAtUtc)` record; `ITokenGenerator.GenerateAccessToken` returns it; `TokenGenerator` takes `TimeProvider`, truncates the expiry to whole seconds and returns it; `TokenResponse` gains `AccessTokenExpiresAtUtc`
  - `LoginUserHandler` maps the new return type (still no refresh persistence); update `LoginUserHandlerTests` and `TokenGeneratorTests`; add `FixedTimeProvider` to `Auth.Tests`
  - Tests: `JwtOptionsTests` (accessors, default 30, `ValidateOnStart` predicate rejects a cap below the window); `ExpiresAtUtc` equals the JWT `exp` and `ValidTo` exactly with a fixed clock; refresh token decodes to 64 bytes
  - _Requirements: 1.1, 1.2, 1.8_

- [x] 2. Design-time DbContext factory (moved from `auth-identity` task 15)
  - `IDesignTimeDbContextFactory<ApplicationDbContext>` in `Auth.Infrastructure`, copied from Availability's/Booking's, so `dotnet ef migrations add` works with a blank connection string
  - Needed by task 5
  - _Requirements: none of this spec's; carried from `auth-identity` 5.2 (migration workflow)_

- [x] 3. `RefreshToken` entity
  - `Auth.Domain/Entities/RefreshToken.cs` per the Data model: private setters, `StartFamily`, `CreateSuccessor` (null at the cap), `IsRevoked`, `IsExpired`
  - `RefreshTokenTests`: expiry = now + 7 d; family id and start set; successor keeps `FamilyId` and `FamilyStartedAtUtc`; `min(sliding, cap)` for a young and an old family; null at or past the cap; `IsExpired` boundary; non-positive spans throw
  - _Requirements: 1.3, 1.4, 1.6, 2.3, 2.4_

- [x] 4. User repository fixes
  - `IUserRepository.GetByIdAsync(Guid, ct)` (no-tracking) and its implementation
  - `UserRepository.IsUniqueViolation` checks `ConstraintName == "IX_Users_Email"` so a token-hash collision is not reported as a duplicate email
  - Update affected tests and mocks
  - _Requirements: 2.1, 2.2, 2.5, 2.6_

- [x] 5. Persistence for refresh tokens
  - `RefreshTokens` DbSet and configuration in `ApplicationDbContext`: `TokenHash` required, max 64, unique index; index on `FamilyId`; required timestamps; cascade FK to `Users`; no FK on `ReplacedById`
  - `IRefreshTokenRepository` (`AddAsync`, `GetByHashAsync`, `TryRotateAsync`, `RevokeFamilyAsync`) in `Application/Abstractions`; `RefreshTokenRepository` per the design (update before insert in one transaction, conditional `ExecuteUpdate` on `RevokedAtUtc IS NULL`, detach the successor); register it scoped
  - `AddRefreshTokens` migration (`dotnet ef migrations add AddRefreshTokens ...`), reviewed against the design's column list; model snapshot updated
  - No automated SQL test (no database harness); covered by task 14
  - _Requirements: 1.5, 2.2, 2.7, 3.2_

- [x] 6. Hash abstraction
  - `IRefreshTokenHasher` in `Application/Abstractions`; `RefreshTokenHasher` in `Infrastructure/Services` (SHA-256 of the UTF-8 bytes, lower-case hex), registered scoped
  - `RefreshTokenHasherTests`: deterministic, 64 hex characters, output does not contain the input, different inputs differ
  - _Requirements: 1.3, 1.4_

- [x] 7. Persist the token at login
  - `LoginUserHandler` takes `IRefreshTokenRepository`, `IRefreshTokenHasher`, `IOptions<JwtOptions>`, `TimeProvider`; builds the family with `RefreshToken.StartFamily`, stages it with `AddAsync`, and commits with the existing `userRepository.SaveChangesAsync` (token and `LastLoginAtUtc` in one commit); response carries `AccessTokenExpiresAtUtc`
  - Tests: success stages exactly one family and saves once; a second login starts a different family; every 401 path never calls `AddAsync`; a non-positive `RefreshTokenExpirationDays` throws and saves nothing; response expiry equals the generator's
  - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.6, 1.7, 1.8_

- [x] 8. Refresh: command, validator, handler
  - `RefreshTokenCommand`, `RefreshTokenRequest(string?)`, `RefreshTokenCommandValidator` (`NotEmpty`), `RefreshTokenCommandHandler` with the 10-step flow of the design (length cap before hashing, revoked-before-expired, family revoke on reuse, inactive user and lost race, access token generated before the rotation, `RecordLogin` not called); register the validator
  - Warnings log the family id only, never the token or hash
  - `RefreshTokenCommandHandlerTests` and `RefreshTokenCommandValidatorTests` as listed in the design's Testing strategy (happy path, each 401 cause, no repository call on 400 or over-256, expired does not revoke, `TryRotateAsync` false revokes the family, access token generated before rotation, logs contain neither token nor hash)
  - _Requirements: 2.1, 2.3, 2.4, 2.5, 2.6, 2.7, 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 3.7_

- [x] 9. Logout: command, validator, handler
  - `LogoutCommand` (`Result<Unit>`), `LogoutRequest(string?)`, `LogoutCommandValidator` (`NotNull`), `LogoutCommandHandler` (empty, whitespace, over-long or unknown -> success without revoke; known -> `RevokeFamilyAsync`); register the validator
  - `LogoutCommandHandlerTests` and `LogoutCommandValidatorTests`: current, rotated, expired and already-revoked tokens revoke the family once; unknown and empty succeed without a write; null is `400`; revoke is keyed on `FamilyId` so other families are untouched
  - _Requirements: 4.1, 4.2, 4.3, 4.6_

- [x] 10. Controller actions and OpenAPI
  - `AuthController.Refresh` and `Logout` (`[AllowAnonymous]`, `ToActionResult` on failure, `Ok(TokenResponse)` / `NoContent()`); `ProducesResponseType` attributes on both and the `200` type on `Login`
  - `AuthControllerTests`: `OkObjectResult` carrying the response; failed results map through `ToActionResult`; `NoContentResult`; both actions allow anonymous (reflection)
  - Model-binding `400`s are expected in the `{status, detail}` shape from `shared-web-infrastructure` Requirement 4.5; add a test only if that shared factory is already on the branch
  - _Requirements: 3.5, 4.4, 4.6, 5.6, 5.7_

- [x] 11. Gateway: routes and `refresh` rate limit
  - `RateLimitPolicies.Refresh = "refresh"`, `RefreshPermits = 10`, `ByClientIp(HttpContext, int permits)` with the one-argument overload kept (login unchanged); register the `refresh` policy in `AddGatewayRateLimiting`
  - `auth-refresh` (with `RateLimiterPolicy: refresh`) and `auth-logout` routes in the gateway's `appsettings.json`: `Order` 0, `anonymous`, `POST`, `RequestHeaderOriginalHost`
  - Update `RoutingConfigurationTests` (eleven routes, new routes, limiter only on `auth-refresh`, others unchanged) and `RateLimitPoliciesTests` (constants; `refresh` permits exactly 10 then rejects; remote-address key, `X-Forwarded-For` ignored; login still 5)
  - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5_

- [x] 12. `.http` samples
  - `SmartAppointments.Gateway.http`: refresh and logout after the login request, with comments on the `429` at the 11th refresh and `401` plus family revoke on replay
  - `Auth.Api.http`: replace the template `weatherforecast` request with login, refresh and logout against `{{Auth.Api_HostAddress}}`
  - _Requirements: 5.1, 5.6_

- [x] 13. Documentation and superseded specs
  - `docs/specs/auth-identity/requirements.md`: mark Requirements 6 and 7 "Superseded by [auth-refresh-tokens](../auth-refresh-tokens/requirements.md)" (keep the text for history); close Known gap 6 with a pointer there
  - `docs/specs/auth-identity/tasks.md`: tasks 16-18 and their dependents 19-21 marked "superseded by [auth-refresh-tokens](../auth-refresh-tokens/tasks.md)" and removed from the open work (intro line updated); task 15 marked "moved to [auth-refresh-tokens](../auth-refresh-tokens/tasks.md) task 2"; `auth-identity/design.md` gets a note at its refresh sections pointing here
  - `docs/specs/README.md`: add an "Auth refresh tokens" row (FR-AUTH-004, FR-AUTH-005) with the three links
  - `docs/requirements.md`: add FR-AUTH-004 and FR-AUTH-005 to section 8 and the endpoints to section 13 (closes Known gaps 5 and 6 of this spec's requirements)
  - `CLAUDE.md`: Auth bullet (refresh and logout, token families, hashed storage, `TimeProvider`, 7-day sliding under a 30-day cap); gateway bullet (nine routes -> eleven, the `refresh` limiter of 10 per client address); Local settings (`Jwt:RefreshTokenFamilyMaxDays`, default 30, ships in `appsettings.json`); a pointer to this spec
  - _Requirements: all (documentation of the superseding spec)_

- [ ] 14. Manual verification against local PostgreSQL
  - Apply the migration (`dotnet ef database update`); compare the schema with the design (columns, unique `IX_RefreshTokens_TokenHash`, `IX_RefreshTokens_FamilyId`, cascade FK); review `dotnet ef migrations script`
  - Start Auth, Availability, Booking and the gateway. Log in, then fire the same refresh token twice in parallel (PowerShell `ForEach-Object -Parallel`), repeated 20 times with fresh logins: exactly one `200` and one `401` each time; afterwards the winner's new token is also `401` and a second login's family still refreshes
  - Replay a rotated token (`401`, family dead); move a token's expiry into the past in the database and refresh (`401`, family not revoked); log out then refresh (`401`); log out twice (`204` both); deactivate a user in the database then refresh (`401`, family revoked, still `401` after reactivating); lower `RefreshTokenFamilyMaxDays` and refresh an old family (`401`)
  - Through the gateway: the 11th refresh from one address in a minute is `429` with `Retry-After: 60` while login permits are untouched; `GET /api/auth/refresh` is `401`; refresh and logout work with no `Authorization` header and with an expired one; `accessTokenExpiresAtUtc` equals the access token's `exp`
  - `/scalar` shows both endpoints and the new field; `dotnet build` and `dotnet test SmartAppointments.slnx` green; record the result in this file
  - _Requirements: 1.2, 2.2, 2.4, 2.7, 3.2, 3.3, 3.4, 3.6, 4.1, 4.2, 4.3, 4.4, 5.2, 5.3, 5.7_

## Deferred

- Purging expired and revoked rows (Known gap 2), a revoke-all-sessions endpoint, and logout rate limiting (Known gap 3).
