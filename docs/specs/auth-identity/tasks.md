# Auth identity — Tasks

> **Retro-fitted spec.** Tasks 1–8 were reconstructed from the commit history after the fact; their boxes are checked because the code exists and `dotnet test SmartAppointments.slnx` passes. Tasks 9–14 came from the *Known gaps* section of [`requirements.md`](requirements.md) and were done as a batch; the test count went from 28 executed cases to 88. Tasks 15–21 were the plan for Requirements 6 and 7; they are no longer open work: task 15 moved to [auth-refresh-tokens](../auth-refresh-tokens/tasks.md) and tasks 16–21 are superseded by it.

## Delivered

- [x] 1. Domain model
  - `User` aggregate with private setters and `RegisterCustomer` / `RegisterStaff` / `RegisterAdmin` factories
  - `Email` value object with normalising `Create` factory
  - `RecordLogin`, `Activate`, `Deactivate`, `ChangePassword` behaviours
  - _Requirements: 1.2, 1.7, 2.5_

- [x] 2. Persistence
  - `ApplicationDbContext` with the `User` configuration, `Email` value converter and unique email index
  - `IUserRepository` with separate staging, no-tracking read, for-update read and commit
  - `UserRepository` with the shared normalising `QueryByEmail`
  - `InitialCreate` migration
  - _Requirements: 1.5, 1.7, 1.8, 1.9, 2.6, 3.7_

- [x] 3. Registration
  - `RegisterCustomerCommand`, `RegisterCustomerCommandHandler`, `RegisterCustomerCommandValidator`
  - `IPasswordHasher` / BCrypt `PasswordHasher`
  - `POST /api/Auth/register` returning `201 Created` with a `Location` header
  - _Requirements: 1.1, 1.3, 1.4, 1.6_

- [x] 4. Token generation
  - `JwtOptions` bound from the `Jwt` configuration section
  - `ITokenGenerator` / `TokenGenerator`: HS256, short claim names, configured lifetime, random refresh token
  - _Requirements: 2.7, 2.8, 2.9, 2.10_

- [x] 5. Login
  - `LoginUserCommand`, `LoginUserHandler`, `LoginUserRequestValidator`
  - Identical 401 for unknown email, inactive account and wrong password
  - `POST /api/Auth/login`
  - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_

- [x] 6. Authentication and authorization wiring
  - `AddAuthentication` with `MapInboundClaims = false`, zero clock skew, `role` as the role claim type
  - `AddAuthorizationWithRoles` registering the five policies
  - Policy and claim names centralised in `BuildingBlocks/Constants.cs`
  - _Requirements: 3.2, 3.9, 3.10_

- [x] 7. Profile retrieval
  - `GetCustomerQuery`, `GetCustomerByEmailHandler` with the customer-scoped narrowing rule
  - `GET /api/Auth/profile` reading the caller's claims and passing them into the query
  - _Requirements: 3.1, 3.3, 3.4, 3.5, 3.6, 3.8_

- [x] 8. Operational wiring
  - `ExceptionMiddleware` and `LoggingMiddleware` registered ahead of auth
  - `/healthz` with the Npgsql check
  - Scalar API reference with the `Bearer` scheme, development only
  - `DatabaseSeeder` bootstrap admin
  - _Requirements: 4.1, 4.2, 4.3_

## Delivered — gap closure

- [x] 9. Move secrets out of source control
  - Remove the JWT `SecretKey` and the PostgreSQL password from `src/Services/Auth/Auth.Api/appsettings.json`
  - Move them to `dotnet user-secrets` for local development and document the keys in `CLAUDE.md`
  - Fail fast at startup with a clear message when either is absent
  - _Known gap 1_

- [x] 10. Map `Result` failures to their actual status codes
  - Switch `AuthController` on `result.Error.Status` instead of returning a fixed result per action
  - Profile: 403 → `Forbid`/`StatusCode(403)`, 404 → `NotFound`
  - Login: 400 → `BadRequest`, 401 → `Unauthorized`
  - Update `AuthControllerTests.GetProfile_WithoutClaims_Returns_NotFound`, which currently asserts the wrong status, and add a test per branch
  - _Known gap 2; Requirements: 2.4, 3.5_

- [x] 11. Add a `GET /api/Auth/me` endpoint
  - Resolve the caller from the token with no email parameter
  - Keep `GET /api/Auth/profile?email=` for the staff and admin lookup case
  - _Known gap 3; Requirements: 3.1, 3.3_

- [x] 12. Handle the duplicate-registration race
  - `UserRepository.SaveChangesAsync` translates a PostgreSQL `23505` unique violation into `DuplicateEmailException`, so the Application layer needs no EF Core reference; `RegisterCustomerCommandHandler` catches it and returns the same `Error(400, "Email is already registered.")` as the pre-check
  - Add a test that the post-commit conflict produces 400 rather than 500
  - _Known gap 4; Requirements: 1.4, 1.9_

- [x] 13. Guard the profile email parameter
  - A blank or `@`-less `email` query value reaches `Email.Create` through the repository and throws `ArgumentException`, which `ExceptionMiddleware` reports as 500
  - Validate it in the handler and return `Error(400, ...)` instead
  - _Requirements: 3.1_

- [x] 14. Close the unit-test gaps
  - `Email` normalisation and rejection rules
  - `PasswordHasher` round-trip and rejection of a wrong password
  - `RegisterCustomerCommandValidator` and `LoginUserRequestValidator` rule by rule
  - `ExceptionMiddleware` status mapping, including that a 500 body does not echo the exception message
  - _Known gap 5. `UserRepository`, `DatabaseSeeder` and `LoggingMiddleware` remain untested: all three need a database or a full request pipeline, which the pure-unit-test convention in [`design.md`](design.md) does not cover. They wait for the integration-test harness._

## Moved and superseded

Nothing in this spec is open. Requirements 6 and 7 are superseded by [auth-refresh-tokens](../auth-refresh-tokens/requirements.md), which models token families rather than a chain; its [tasks](../auth-refresh-tokens/tasks.md) replace the items below. They are listed without checkboxes so they do not read as open work.

- 15. Design-time DbContext factory — moved to [auth-refresh-tokens](../auth-refresh-tokens/tasks.md) task 2 (it is not about refresh tokens, and the migration workflow needs it).
- 16. `RefreshToken` aggregate — superseded by [auth-refresh-tokens](../auth-refresh-tokens/tasks.md) task 3.
- 17. Persistence for refresh tokens — superseded by [auth-refresh-tokens](../auth-refresh-tokens/tasks.md) task 5.
- 18. Hash refresh tokens — superseded by [auth-refresh-tokens](../auth-refresh-tokens/tasks.md) task 6.
- 19. Persist the token issued at login — superseded by [auth-refresh-tokens](../auth-refresh-tokens/tasks.md) task 7.
- 20. `POST /api/Auth/refresh` — superseded by [auth-refresh-tokens](../auth-refresh-tokens/tasks.md) task 8.
- 21. `POST /api/Auth/logout` — superseded by [auth-refresh-tokens](../auth-refresh-tokens/tasks.md) task 9.

## Deferred

Recorded so they are not rediscovered as surprises. None is specced yet.

- **Integration-test harness.** `UserRepository`, `RefreshTokenRepository`, `DatabaseSeeder` and `LoggingMiddleware` all need a real PostgreSQL instance or a full request pipeline. Testcontainers plus `WebApplicationFactory` is the intended shape; until then these stay uncovered rather than being mock-tested into meaninglessness.
- **A non-generic `Result`.** `LogoutCommandHandler` returns `Result<bool>` because `BuildingBlocks` has no valueless result type. Adding one touches every service, so it wants its own change.
- **MediatR validation pipeline behaviour.** See *Known gaps* item 7 in [`requirements.md`](requirements.md).
- **Access-token revocation.** Out of scope by decision (Req 7.6): a denylist checked on every request in every service is a different architecture.
