# Auth identity — Requirements

> **Retro-fitted spec.** This was written after the Auth service was built. Every acceptance criterion below was derived from code that exists in `src/Services/Auth/` and, where noted, from a test in `tests/Auth.Tests/`. Where the implementation differs from `docs/requirements.md`, the discrepancy is recorded rather than smoothed over.

## Introduction

The Auth service owns user identity for the whole system: it is the only service that stores credentials and the only issuer of JWTs. This spec covers customer self-registration, credential login, profile retrieval, the session lifecycle that refresh and logout give those tokens, and the role-based authorization scheme every other service will consume.

These belong in one spec rather than several because they share the `User` aggregate and a single design: the same `Result<T>` error contract, the same repository staging rules, the same claim names. It refines sections 8 (Authentication), 9 and 10 of [`docs/requirements.md`](../../requirements.md) and covers `FR-AUTH-001` through `FR-AUTH-005`.

Requirements 1–5 are built and tested. Requirements 6 and 7 are approved and not yet built — [`tasks.md`](tasks.md) tracks which is which.

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
4. WHERE the duplicate is only discovered at commit time because a concurrent registration won the race, the system SHALL return the same `400 Bad Request` rather than surfacing the unique-index violation as a `500`. *(1.4a)*
   _Test: `RegisterCustomerCommandHandlerTests.Duplicate_Detected_Only_At_Commit_Returns_400_Not_500`_
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
4. IF the email is missing or malformed, or the password is missing, THEN the system SHALL return `400 Bad Request` listing the failed rules — not the `401` that this endpoint returns for a credential failure.
   _Test: `AuthControllerTests.Login_ValidationFailure_Returns_BadRequest`_
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
1. WHEN an authenticated caller GETs `/api/Auth/me`, THEN the system SHALL resolve the subject from the token's `email` claim, take no email parameter, and return the same body. *(3.1a)*
   _Test: `AuthControllerTests.GetMe_Resolves_The_Caller_From_The_Token`_
1. IF the resolved email is blank or contains no `@`, THEN the system SHALL return `400 Bad Request` and SHALL NOT query the database. *(3.1b)*
   _Tests: `GetCustomerByEmailHandlerTests.Malformed_Requested_Email_Returns_400_Without_Querying`, `AuthControllerTests.GetProfile_MalformedEmail_Returns_BadRequest`_
2. IF the caller presents no token, or a token whose role is none of `Customer`, `Staff`, `Admin`, THEN the system SHALL reject the request at the authorization policy before the handler runs.
3. WHERE the caller's role is `Customer`, the system SHALL ignore the requested email and return the caller's own profile, so that one customer cannot read another's details.
   _Test: `GetCustomerByEmailHandlerTests.Customer_Requesting_Another_Address_Gets_Their_Own_Profile`_
4. WHERE the caller's role is `Staff` or `Admin`, the system SHALL return the profile for the requested email.
   _Test: `GetCustomerByEmailHandlerTests.Admin_Reads_The_Requested_Address`_
5. IF the token is missing the `email` or `role` claim, THEN the system SHALL deny the request with `403 Forbidden` and SHALL NOT query the database.
   _Tests: `GetCustomerByEmailHandlerTests.Missing_Caller_Claims_Are_Denied`, `AuthControllerTests.GetProfile_WithoutClaims_Returns_Forbidden`, `AuthControllerTests.GetMe_WithoutClaims_Returns_Forbidden`_
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

### Requirement 5: Keep credentials out of source control

**User Story:** As an operator, I want every secret the service needs to come from outside the repository, so that cloning the repository does not hand anyone a signing key or a database password.

*(No FR-ID — this refines the security non-functional requirements in [`docs/requirements.md`](../../requirements.md).)*

#### Acceptance Criteria

1. WHERE a configuration value is a secret — the database connection string and the JWT signing key — the checked-in `appsettings.json` SHALL carry an empty placeholder and nothing more.
2. IF `ConnectionStrings:DefaultConnection` is absent or blank at startup, THEN the system SHALL throw an `InvalidOperationException` naming the key and the `dotnet user-secrets` command that sets it, rather than starting and failing on the first request.
3. IF `Jwt:SecretKey` is absent or blank at startup, THEN the system SHALL throw the equivalent exception, and SHALL NOT fall back to any default key.
4. IF `Jwt:SecretKey` is shorter than 32 bytes, THEN the system SHALL refuse to start, because a shorter key does not safely carry HMAC-SHA256.
5. IF `Jwt:Issuer` or `Jwt:Audience` is blank, THEN the system SHALL refuse to start.

### Requirement 6: Redeem and rotate a refresh token (FR-AUTH-004)

**User Story:** As a signed-in user, I want a short-lived access token to be renewed silently in the background, so that I stay signed in for a working day without my credentials being held by the client or re-entered every hour.

*(`docs/requirements.md` names a refresh token in the auth section but specifies no redemption flow. `FR-AUTH-004` is claimed here and should be written back into the BRD.)*

#### Acceptance Criteria

1. WHEN a login succeeds, THEN the system SHALL persist the issued refresh token alongside the user it belongs to, the UTC time it was issued, and an expiry of `Jwt:RefreshTokenExpirationDays` days from issue.
2. WHERE a refresh token is persisted, the system SHALL store a SHA-256 hash of it and never the token itself, so that read access to the database does not confer the ability to mint access tokens.
3. WHEN an anonymous caller POSTs a refresh token to `/api/Auth/refresh`, AND that token is found, unexpired, unrevoked and belongs to an active user, THEN the system SHALL return `200 OK` with a new `{ AccessToken, RefreshToken }` pair.
4. WHEN a refresh token is redeemed, THEN the system SHALL revoke it and record the replacement that supersedes it, committing both in the same transaction that issues the replacement, so that each token is redeemable exactly once.
5. IF the presented token is unknown, expired, already redeemed, revoked, or belongs to an inactive user, THEN the system SHALL return `401 Unauthorized` with a single undifferentiated message, for the same account-enumeration reason as Requirement 2.3.
6. WHEN a token that has already been redeemed is presented a second time, THEN the system SHALL additionally revoke every token descended from it, because a second presentation means either the token leaked or the client is replaying, and neither is safe to keep alive.
7. WHEN a login or a refresh succeeds, THEN the system SHALL delete that user's already-expired refresh tokens, so that the table stays bounded without a scheduled job.
8. WHEN a refresh token is generated, THEN the system SHALL continue to use the 64 cryptographically random bytes of Requirement 2.10 — this requirement changes what happens to the token, not how it is made.
9. WHERE a user signs in on more than one device, the system SHALL allow them to hold several live refresh tokens at once, one per login.

*Deliberately excluded:* no grace window for concurrent redemption. Two clients redeeming the same token within milliseconds is indistinguishable from the replay of criterion 6 and is treated as one, which is the stricter and simpler reading.

### Requirement 7: Log out (FR-AUTH-005)

**User Story:** As a signed-in user, I want to log out, so that the session I am ending cannot be resumed from the device I am leaving.

#### Acceptance Criteria

1. WHEN a caller POSTs a refresh token to `/api/Auth/logout`, THEN the system SHALL revoke that token so that Requirement 6 criterion 5 rejects any later attempt to redeem it.
2. WHEN logout revokes a token, THEN the system SHALL revoke only the token presented, and SHALL leave that user's other live refresh tokens alone, so that signing out on one device does not end the session on another (Req 6.9).
3. WHEN logout is called, THEN the system SHALL return `204 No Content` whether or not the presented token existed, was already revoked, or had expired, so that the endpoint is idempotent and cannot be used to discover which tokens are live.
4. WHERE a token was revoked by logout rather than redeemed, presenting it to `/api/Auth/refresh` SHALL fail under Requirement 6 criterion 5 but SHALL NOT trigger the chain revocation of criterion 6 — an explicit logout is not evidence of a leak, and a logged-out token has no descendants in any case.
5. WHERE the caller's access token has already expired, the system SHALL still accept the logout, because possession of the refresh token is the only credential the endpoint needs and refusing would leave the token live.
6. WHEN a logout succeeds, THEN the system SHALL NOT invalidate any access token already issued — those remain valid until they expire, which is the accepted cost of stateless JWT validation and is bounded by `Jwt:AccessTokenExpirationMinutes`.

## Discrepancies with the BRD

| BRD | Implementation | Decision |
|---|---|---|
| `GET /api/auth/me` | `GET /api/Auth/me` **and** `GET /api/Auth/profile?email=` | Resolved. `/me` now exists and takes no parameter (Req 3.1a); `/profile?email=` remains for the staff and admin lookup case, with the customer case still forced back to "me" in the handler (Req 3.3). Both dispatch the same `GetCustomerQuery`. |
| Routes are lower-case in the BRD (`/api/auth/...`) | The controller uses `[Route("api/[controller]")]`, so the route is `/api/Auth/...` | Cosmetic, but ASP.NET routing is case-insensitive on match, so both forms work. Left as-is. |
| FR-AUTH-001 says "full name" | Stored as separate `FirstName` / `LastName`, with a computed `FullName` | The implementation is the better model. No change. |

## Out of scope

These exist in the code but are not requirements yet, because nothing reachable calls them. They are recorded here so they are not mistaken for delivered features:

- **Staff and admin registration** — `User.RegisterStaff` and `User.RegisterAdmin` exist; only `RegisterAdmin` has a caller (the seeder), and there is no endpoint for either.
- **Account activation and deactivation** — `User.Deactivate()` / `User.Activate()` exist with no caller. Login already refuses inactive accounts (Req 2.3), so only the administrative action is missing.
- **Password change** — `User.ChangePassword(hash)` exists with no caller, no endpoint, and no reset/forgot-password flow.
- ~~**Refresh token redemption**~~ — moved into scope as Requirement 6.
- ~~**Logout / token revocation**~~ — moved into scope as Requirement 7, limited to refresh-token revocation. Access tokens remain valid until they expire (Req 7.6); revoking those would mean a denylist checked on every request in every service, which is a different design decision and is not being made here.

## Known gaps

Gaps 1–5 have been closed; each has a matching checked item in [`tasks.md`](tasks.md). They are kept here rather than deleted so the record of what was wrong, and how it was fixed, survives.

1. ~~**Secrets are committed.**~~ Closed. `appsettings.json` now carries empty placeholders and the service fails fast on either missing value — see Requirement 5. The keys and the `dotnet user-secrets` commands are documented in `CLAUDE.md`.
2. ~~**The profile endpoint reports 403 as 404.**~~ Closed. `AuthController` routes every failure through a single `ToErrorResult(Error)` that switches on `Error.Status`, so 400, 401, 403, 404 and 409 each reach the caller intact. This is the shape every future controller should copy.
3. ~~**No `/me` route.**~~ Closed. See the discrepancy table.
4. ~~**Registration relies on a read-then-write duplicate check.**~~ Closed, though the pre-check itself remains — it is still the cheap common path and avoids a wasted BCrypt hash. The race is handled at the other end: `UserRepository` translates PostgreSQL SQLSTATE `23505` into `DuplicateEmailException`, which the handler converts into the same 400 (Req 1.4a).
5. ~~**Several units are untested.**~~ Mostly closed. `Email`, `PasswordHasher`, `ExceptionMiddleware` and both validators now have dedicated test classes, and the suite went from 28 executed cases to 88. Still untested: `UserRepository`, `DatabaseSeeder` and `LoggingMiddleware`, each of which needs a real database or a full request pipeline. They are deferred to an integration-test harness rather than forced into the pure-unit-test convention.

### Still open

6. **The refresh token is issued but unredeemable.** Specified as Requirements 6 and 7; see [`tasks.md`](tasks.md) tasks 15–16. Open until those ship.
7. **No MediatR validation pipeline behaviour.** Each handler calls its own validator, so a future handler that forgets to is silently unvalidated. A `ValidationBehavior<TRequest, TResponse>` in `Auth.Application` would make validation structural rather than a convention. Not yet specced.
