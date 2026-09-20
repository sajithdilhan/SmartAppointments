# Auth identity — Tasks

> **Retro-fitted spec.** Tasks 1–8 were reconstructed from the commit history after the fact; their boxes are checked because the code exists and `dotnet test SmartAppointments.slnx` passes. Tasks 9 onwards are the real backlog — they come from the *Known gaps* section of [`requirements.md`](requirements.md) and have never been done.

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

## Backlog

- [ ] 9. Move secrets out of source control
  - Remove the JWT `SecretKey` and the PostgreSQL password from `src/Services/Auth/Auth.Api/appsettings.json`
  - Move them to `dotnet user-secrets` for local development and document the keys in `CLAUDE.md`
  - Fail fast at startup with a clear message when either is absent
  - _Known gap 1_

- [ ] 10. Map `Result` failures to their actual status codes
  - Switch `AuthController` on `result.Error.Status` instead of returning a fixed result per action
  - Profile: 403 → `Forbid`/`StatusCode(403)`, 404 → `NotFound`
  - Login: 400 → `BadRequest`, 401 → `Unauthorized`
  - Update `AuthControllerTests.GetProfile_WithoutClaims_Returns_NotFound`, which currently asserts the wrong status, and add a test per branch
  - _Known gap 2; Requirements: 2.4, 3.5_

- [ ] 11. Add a `GET /api/Auth/me` endpoint
  - Resolve the caller from the token with no email parameter
  - Keep `GET /api/Auth/profile?email=` for the staff and admin lookup case
  - _Known gap 3; Requirements: 3.1, 3.3_

- [ ] 12. Handle the duplicate-registration race
  - Catch `DbUpdateException` on the unique email index in `RegisterCustomerCommandHandler` and return the same `Error(400, "Email is already registered.")` as the pre-check
  - Add a test that the post-commit conflict produces 400 rather than 500
  - _Known gap 4; Requirements: 1.4, 1.9_

- [ ] 13. Guard the profile email parameter
  - A blank or `@`-less `email` query value reaches `Email.Create` through the repository and throws `ArgumentException`, which `ExceptionMiddleware` reports as 500
  - Validate it in the handler and return `Error(400, ...)` instead
  - _Requirements: 3.1_

- [ ] 14. Close the unit-test gaps
  - `Email` normalisation and rejection rules
  - `PasswordHasher` round-trip and rejection of a wrong password
  - `RegisterCustomerCommandValidator` and `LoginUserRequestValidator` rule by rule
  - `ExceptionMiddleware` status mapping
  - _Known gap 5_

- [ ] 15. Decide the fate of the refresh token
  - Either implement redemption — persist the token with its expiry, add `POST /api/Auth/refresh`, rotate on use — or stop issuing it and drop `RefreshTokenExpirationDays`
  - Issuing a token nothing accepts is the current state and is worse than either option
  - _Requirements: 2.10; requires a requirements amendment before implementing_
