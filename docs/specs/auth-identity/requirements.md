# Auth identity — Requirements

> **Retro-fitted spec.** This was written after the Auth service was built. Every acceptance criterion below was derived from code that exists in `src/Services/Auth/` and, where noted, from a test in `tests/Auth.Tests/`. Where the implementation differs from `docs/requirements.md`, the discrepancy is recorded rather than smoothed over.

## Introduction

The Auth service owns user identity for the whole system: it is the only service that stores credentials and the only issuer of JWTs. This spec covers the three capabilities that are implemented today — customer self-registration, credential login, and profile retrieval — plus the role-based authorization scheme every other service will consume.

Registration, login and profile reads share the `User` aggregate and a single design, which is why they form one spec rather than three. It refines sections 8 (Authentication), 9 and 10 of [`docs/requirements.md`](../../requirements.md) and covers `FR-AUTH-001`, `FR-AUTH-002` and `FR-AUTH-003`.

## Requirements

### Requirement 1: Register a customer (FR-AUTH-001)

**User Story:** As a prospective customer, I want to register with my name, email, phone number and a password, so that I can book appointments under my own identity.

#### Acceptance Criteria

1. WHEN an anonymous caller POSTs a valid `RegisterCustomerRequest` to `/api/Auth/register`, THEN the system SHALL create a user with the `Customer` role and return `201 Created` with a `Location` header pointing at the profile endpoint and a body of `{ UserId, Email, Role }`.
   _Test: `AuthControllerTests.Register_Successful_Returns_Created`_
2. WHEN a user is created, THEN the system SHALL assign a version-7 GUID as its identifier, set `RegistrationDateUtc` to the current UTC time, and set `IsActive` to true.
3. WHEN a registration request is received, THEN the system SHALL reject it with `400 Bad Request` and a message listing every failed rule IF any of the following does not hold:
   - `FirstName` and `LastName` are non-empty and at most 50 characters;
   - `Email` is non-empty and a valid email address;
   - `PhoneNumber` is non-empty and matches E.164 (`^\+?[1-9]\d{1,14}$`);
   - `Password` is at least 8 characters and contains an uppercase letter, a lowercase letter, a digit and a non-word character.
   _Test: `RegisterCustomerCommandHandlerTests.Invalid_Request_Is_Rejected_Without_Writing`_
4. IF the email is already registered, THEN the system SHALL return `400 Bad Request` with "Email is already registered." and SHALL NOT write to the database.
   _Tests: `RegisterCustomerCommandHandlerTests.Duplicate_Email_Is_Rejected_Without_Writing`, `AuthControllerTests.Register_Failure_Returns_BadRequest`_
5. WHEN checking for a duplicate, THEN the system SHALL use the read-only (no-tracking) query, not the for-update query.
   _Test: `RegisterCustomerCommandHandlerTests.Duplicate_Check_Uses_The_Read_Only_Query`_
6. WHEN a password is stored, THEN the system SHALL store only a BCrypt hash and SHALL NOT persist or log the plaintext.
7. WHEN an email is stored or looked up, THEN the system SHALL normalise it by trimming whitespace and lower-casing, and SHALL reject a value that is empty or contains no `@`.
8. WHEN a registration is persisted, THEN the system SHALL stage the entity and commit it in a separate, explicit call, so that the write can be made atomic with future work in the same transaction.
   _Test: `RegisterCustomerCommandHandlerTests.Registration_Stages_Then_Commits_Explicitly`_
9. WHERE two users would share an email address, the database SHALL reject the second one via a unique index.

### Requirement 2: Log in (FR-AUTH-002)

**User Story:** As a registered user, I want to log in with my email and password, so that I receive a JWT I can present to the other services.

#### Acceptance Criteria

1. WHEN an anonymous caller POSTs valid credentials for an active account to `/api/Auth/login`, THEN the system SHALL return `200 OK` with `{ AccessToken, RefreshToken }`.
   _Tests: `AuthControllerTests.Login_Successful_Returns_Ok`, `LoginUserHandlerTests.Any_Active_Role_Can_Log_In`_
2. WHERE the account holds any of the `Customer`, `Staff` or `Admin` roles, the system SHALL allow login — the endpoint is not restricted to customers.
   _Test: `LoginUserHandlerTests.Any_Active_Role_Can_Log_In` (theory over all three roles)_
3. IF the email is unknown, OR the account is inactive, OR the password does not verify, THEN the system SHALL return `401 Unauthorized` with the single message "Invalid user or password.", so that the endpoint cannot be used to discover which addresses are registered.
   _Tests: `LoginUserHandlerTests.Unknown_Email_Returns_401_Not_404`, `Inactive_User_Is_Rejected`, `Wrong_Password_Is_Rejected`, `AuthControllerTests.Login_Failure_Returns_Unauthorized`_
4. IF the email is missing or malformed, or the password is missing, THEN the system SHALL return `400 Bad Request` listing the failed rules.
5. WHEN a login succeeds, THEN the system SHALL set `LastLoginAtUtc` to the current UTC time and persist it.
   _Test: `LoginUserHandlerTests.Successful_Login_Records_The_Login_Timestamp`_
6. WHEN reading the user during login, THEN the system SHALL use the for-update (tracked) query, because the login timestamp is about to be mutated.
7. WHEN an access token is issued, THEN the system SHALL sign it with HMAC-SHA256 using the configured secret, set the configured issuer and audience, and expire it after the configured number of minutes.
   _Tests: `TokenGeneratorTests.GenerateAccessToken_Honours_Configured_Expiration`, `GenerateAccessToken_Sets_Issuer_And_Audience`_
8. WHEN an access token is issued, THEN the system SHALL emit exactly the claims `sub` (user id), `email` and `role`, using those short names verbatim rather than the SOAP-style URIs.
   _Test: `TokenGeneratorTests.GenerateAccessToken_Emits_Short_Claim_Names`_
9. IF `AccessTokenExpirationMinutes` is not configured as a positive number, THEN the system SHALL throw rather than issue a token with a default or unbounded lifetime.
   _Test: `TokenGeneratorTests.GenerateAccessToken_Throws_When_Expiration_Not_Configured`_
10. WHEN a refresh token is issued, THEN the system SHALL generate 64 cryptographically random bytes and return them base64-encoded. (See *Out of scope* — nothing consumes this token yet.)

### Requirement 3: Retrieve a profile under role-based authorization (FR-AUTH-003)

**User Story:** As a signed-in user, I want to read a profile, so that I can see my own details — and, if I am staff or an administrator, the details of the customers I am serving.

#### Acceptance Criteria

1. WHEN an authenticated caller holding any of the three roles GETs `/api/Auth/profile?email=...`, THEN the system SHALL return `200 OK` with `{ FirstName, LastName, Email, PhoneNumber, IsActive }`.
   _Test: `AuthControllerTests.GetProfile_Successful_Returns_Ok`_
2. IF the caller presents no token, or a token whose role is none of `Customer`, `Staff`, `Admin`, THEN the system SHALL reject the request at the authorization policy before the handler runs.
3. WHERE the caller's role is `Customer`, the system SHALL ignore the requested email and return the caller's own profile, so that one customer cannot read another's details.
   _Test: `GetCustomerByEmailHandlerTests.Customer_Requesting_Another_Address_Gets_Their_Own_Profile`_
4. WHERE the caller's role is `Staff` or `Admin`, the system SHALL return the profile for the requested email.
   _Test: `GetCustomerByEmailHandlerTests.Admin_Reads_The_Requested_Address`_
5. IF the token is missing the `email` or `role` claim, THEN the system SHALL deny the request and SHALL NOT query the database.
   _Tests: `GetCustomerByEmailHandlerTests.Missing_Caller_Claims_Are_Denied`, `AuthControllerTests.GetProfile_WithoutClaims_Returns_NotFound`_
6. IF no user exists for the resolved email, THEN the system SHALL return `404 Not Found`.
   _Tests: `GetCustomerByEmailHandlerTests.Unknown_Address_Returns_404`, `AuthControllerTests.GetProfile_NotFound_Returns_NotFound`_
7. WHEN reading a profile, THEN the system SHALL use the read-only (no-tracking) query.
   _Test: `GetCustomerByEmailHandlerTests.Profile_Read_Uses_The_Read_Only_Query`_
8. WHEN the caller's claims are read, THEN the system SHALL read them under the short names `email` and `role` and pass them to the handler explicitly, rather than letting the handler reach into `HttpContext`.
   _Test: `AuthControllerTests.GetProfile_Passes_Caller_Claims_To_The_Query`_
9. WHEN a bearer token is validated, THEN the system SHALL validate issuer, audience, lifetime and signing key with zero clock skew, and SHALL treat the `role` claim as the role claim type.
10. WHEN the service starts, THEN the system SHALL make available the policies `AdminPolicy`, `StaffPolicy`, `CustomerPolicy`, `AdminOrStaffPolicy` and `AllowedOriginsPolicy` for use by this and future controllers.

### Requirement 4: Bootstrap an administrator

**User Story:** As an operator deploying the system for the first time, I want an administrator account to exist, so that I can exercise the admin-only endpoints without hand-editing the database.

*(No FR-ID — this is an operational need the BRD does not cover. Consider adding one.)*

#### Acceptance Criteria

1. WHEN the service starts AND a complete `Seed:Admin` configuration section is present AND no user with the `Admin` role exists, THEN the system SHALL create one administrator from that configuration.
2. IF the `Seed:Admin` section is absent or incomplete, THEN the system SHALL start normally without seeding.
3. IF any administrator already exists, THEN the system SHALL NOT create another.

## Discrepancies with the BRD

| BRD | Implementation | Decision |
|---|---|---|
| `GET /api/auth/me` | `GET /api/Auth/profile?email=` | The implemented endpoint is broader: it serves the caller's own profile *and* staff/admin lookups of others, with the customer case forced back to "me" in the handler. Requirement 3 documents the endpoint as built. A dedicated `/me` route that takes no parameter would be a clearer API and is listed as a known gap. |
| Routes are lower-case in the BRD (`/api/auth/...`) | The controller uses `[Route("api/[controller]")]`, so the route is `/api/Auth/...` | Cosmetic, but ASP.NET routing is case-insensitive on match, so both forms work. Left as-is. |
| FR-AUTH-001 says "full name" | Stored as separate `FirstName` / `LastName`, with a computed `FullName` | The implementation is the better model. No change. |

## Out of scope

These exist in the code but are not requirements yet, because nothing reachable calls them. They are recorded here so they are not mistaken for delivered features:

- **Staff and admin registration** — `User.RegisterStaff` and `User.RegisterAdmin` exist; only `RegisterAdmin` has a caller (the seeder), and there is no endpoint for either.
- **Account activation and deactivation** — `User.Deactivate()` / `User.Activate()` exist with no caller. Login already refuses inactive accounts (Req 2.3), so only the administrative action is missing.
- **Password change** — `User.ChangePassword(hash)` exists with no caller, no endpoint, and no reset/forgot-password flow.
- **Refresh token redemption** — a refresh token is generated and returned (Req 2.10) and `JwtOptions.RefreshTokenExpirationDays` is configured, but the token is never persisted and no endpoint accepts it. It is currently an opaque string the client can do nothing with.
- **Logout / token revocation** — no server-side token invalidation of any kind.

## Known gaps

Each has a matching unchecked item in [`tasks.md`](tasks.md).

1. **Secrets are committed.** The JWT `SecretKey` and the PostgreSQL password are hard-coded in `src/Services/Auth/Auth.Api/appsettings.json`, which is in source control. They belong in user-secrets locally and in environment variables or a secret store elsewhere.
2. **The profile endpoint reports 403 as 404.** `GetCustomerByEmailHandler` returns `Error(403, "Permission denied.")` when the caller's claims are missing, but `AuthController.GetProfile` maps every failure to `NotFound`, so the status is lost. The controller should switch on `result.Error.Status`. `AuthControllerTests.GetProfile_WithoutClaims_Returns_NotFound` currently asserts the wrong behaviour and would need updating with the fix.
3. **No `/me` route.** See the discrepancy table — a customer must pass an email parameter that is then ignored.
4. **Registration relies on a read-then-write duplicate check.** Two concurrent registrations for the same address both pass the check and the second fails on the unique index as an unhandled `DbUpdateException`, surfacing as a 500 rather than the 400 of Req 1.4.
5. **Several units are untested**: `Email`, `PasswordHasher`, `UserRepository`, `DatabaseSeeder`, `ExceptionMiddleware`, `LoggingMiddleware`, and both validators in isolation.
