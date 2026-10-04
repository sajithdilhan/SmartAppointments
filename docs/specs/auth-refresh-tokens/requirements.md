# Auth refresh tokens — Requirements

## Introduction

The Auth service already issues a random `refreshToken` at login, but nothing stores it and nothing accepts it, so a signed-in user is signed out when the one-hour access token expires. This feature makes the refresh token real for a separately hosted Angular SPA that keeps the access token in memory and the refresh token in `localStorage`: the refresh token is stored (hashed) as the start of a token family, can be redeemed once at `POST /api/auth/refresh` for a new pair, and can be revoked at `POST /api/auth/logout`. Reuse of a rotated token revokes the whole family. The refresh token travels in the JSON body, never in a cookie.

Auth owns the feature; the API gateway gains two anonymous routes and one rate-limit policy. `docs/requirements.md` mentions a refresh token only in passing and defines no flow, so the FR-IDs `FR-AUTH-004` (refresh) and `FR-AUTH-005` (logout) are the ones already claimed in [`auth-identity/requirements.md`](../auth-identity/requirements.md) (Requirements 6 and 7) and should be written back into the BRD. Login behaviour from `auth-identity` Requirement 2 is unchanged except where Requirement 1 below says so.

**This spec supersedes `auth-identity` Requirements 6 and 7.** Those were approved but never built (tasks 15-18 of `auth-identity/tasks.md` are unchecked), and they differ from the agreed design here: they model a chain of tokens instead of a family, revoke only descendants on replay, make logout revoke a single token, delete expired rows on every login and refresh, and have no absolute lifetime. When this spec is approved, those two requirements and the open tasks should be replaced with a pointer here (see Known gaps). Error bodies follow [`shared-web-infrastructure` Requirement 4](../shared-web-infrastructure/requirements.md): `{"status":<int>,"detail":"..."}`.

## Requirements

### Requirement 1: Persist the refresh token at login (FR-AUTH-004)

**User Story:** As a signed-in user, I want the refresh token I receive at login to be remembered by the server, so that it can later be exchanged for a new access token.

#### Acceptance Criteria

1. WHEN `POST /api/auth/login` succeeds, THEN the system SHALL return `200 OK` with a body containing `accessToken`, `refreshToken` and `accessTokenExpiresAtUtc`.
2. WHEN a login succeeds, THEN `accessTokenExpiresAtUtc` SHALL be the same instant as the `exp` claim of the returned access token, formatted as ISO-8601 UTC with a trailing `Z` (for example `2026-10-04T13:00:00Z`).
3. WHEN a login succeeds, THEN the system SHALL generate the refresh token as 64 cryptographically random bytes, base64-encoded, and SHALL persist a new refresh-token record holding the user id, a new family id, the SHA-256 hash of the token, the issue time (UTC), and an expiry of `Jwt:RefreshTokenExpirationDays` (7) days after issue.
4. WHERE a refresh token is persisted, the system SHALL store only its hash, SHALL NOT store the raw value in any column, and SHALL NOT write it to any log.
5. WHERE the hash of two refresh tokens would be equal, the database SHALL reject the second one via a unique index on the hash.
6. WHEN the same user logs in again (another device or tab), THEN the system SHALL start a separate new family and SHALL leave the user's existing families untouched, so several sessions can be live at once.
7. IF the email is unknown, OR the account is inactive, OR the password does not verify, THEN the system SHALL return `401 Unauthorized` with "Invalid user or password." and SHALL NOT persist a refresh token (unchanged from `auth-identity` Requirement 2.3).
8. IF `Jwt:RefreshTokenExpirationDays` is not configured as a positive number, THEN the system SHALL throw rather than issue a token with a default or unbounded lifetime.

### Requirement 2: Redeem and rotate a refresh token (FR-AUTH-004)

**User Story:** As a signed-in user, I want my access token renewed silently with my refresh token, so that I stay signed in across the working day without re-entering my password.

#### Acceptance Criteria

1. WHEN an anonymous caller (no `Authorization` header) POSTs `{"refreshToken":"<token>"}` to `/api/auth/refresh` AND the token exists, is unexpired and unrevoked AND its user is active, THEN the system SHALL return `200 OK` with a body of the same shape as Requirement 1.1: a new `accessToken`, a new `refreshToken` and its `accessTokenExpiresAtUtc`.
2. WHEN a token is redeemed, THEN the system SHALL mark the presented token revoked (with the time), and SHALL persist the new refresh token in the same family with its own hash, in one transaction, so that a failure leaves the presented token unredeemed.
3. WHEN the new refresh token is issued, THEN its expiry SHALL be the earlier of "issue time + `Jwt:RefreshTokenExpirationDays` days" and "family start time + `Jwt:RefreshTokenFamilyMaxDays` days" (default 30; a sliding window under an absolute cap). The family start time is the login that created the family and is carried unchanged through every rotation.
4. IF the redeemed token's family is already at its absolute cap so that the computed expiry is not after the issue time, THEN the system SHALL return `401 Unauthorized` per Requirement 3.1, so that a session cannot outlive 30 days without a new login.
5. WHEN a token is redeemed, THEN the new access token SHALL carry the user's current `email` and `role` as stored at refresh time (not those of the earlier token), using the same claims, signing and lifetime as at login (`auth-identity` Requirement 2.7-2.8).
6. WHEN a refresh succeeds, THEN the system SHALL NOT change the user's `LastLoginAtUtc`.
7. WHEN two requests present the same unrevoked token at the same moment, THEN exactly one SHALL succeed (`200`) and the other SHALL be handled as a reuse under Requirement 3.2; the revocation SHALL be an atomic conditional update, not a read followed by a write.

### Requirement 3: Reject invalid tokens and detect reuse (FR-AUTH-004)

**User Story:** As a user, I want a stolen refresh token to stop working for everyone as soon as it is used twice, so that a leaked token cannot silently outlive my own session.

#### Acceptance Criteria

1. IF the presented token is unknown, malformed, longer than 256 characters, expired, or belongs to a user that is now inactive or no longer exists, THEN the system SHALL return `401 Unauthorized` with the single message "Invalid or expired refresh token.", so that the endpoint cannot be used to discover which tokens exist or why one failed.
2. IF the presented token is already revoked (rotated by a refresh, or revoked by logout, or revoked by an earlier reuse), THEN the system SHALL revoke every not-yet-revoked token in its family, SHALL return `401 Unauthorized` with the message of criterion 1, and SHALL NOT issue any token.
3. WHEN a family is revoked under criterion 2, THEN both the latest token the legitimate client holds and any token an attacker obtained by rotating SHALL fail their next redemption with `401`.
4. IF the token is valid but its user is inactive, THEN the system SHALL also revoke the token's whole family, so that reactivating the account does not resurrect an old session.
5. IF the `refreshToken` field is missing, null, empty or whitespace, THEN the system SHALL return `400 Bad Request` with a message listing the failed rule (the same "Invalid request: ..." style as login), and SHALL NOT query the database.
6. WHERE the token is expired but not revoked, the system SHALL return `401` under criterion 1 and SHALL NOT revoke the family (expiry is not evidence of a leak).
7. WHEN any 401 of this requirement is returned, THEN the system SHALL log a warning with the family id where one is known and SHALL NOT log the token or its hash.

### Requirement 4: Log out (FR-AUTH-005)

**User Story:** As a signed-in user, I want to log out, so that the session I am ending cannot be resumed from the browser I leave.

#### Acceptance Criteria

1. WHEN an anonymous caller POSTs `{"refreshToken":"<token>"}` to `/api/auth/logout` AND the token exists, THEN the system SHALL revoke every not-yet-revoked token in that token's family (whether the presented token is the current one, an older rotated one, or expired) and return `204 No Content` with no body.
2. IF the token is unknown, malformed, empty or already revoked, THEN the system SHALL still return `204 No Content`, so that the endpoint is idempotent and cannot be used to discover which tokens exist.
3. WHEN logout revokes a family, THEN the system SHALL leave the user's other families (other logins) alone.
4. WHEN logout is called, THEN no `Authorization` header SHALL be required, and an expired or invalid access token on the request SHALL NOT cause a rejection (possession of the refresh token is the only credential).
5. WHEN logout succeeds, THEN access tokens already issued SHALL remain valid until they expire. This is the accepted cost of stateless JWT validation, bounded by `Jwt:AccessTokenExpirationMinutes` (60); a denylist checked by every service is a different design and is not made here.
6. IF the `refreshToken` field is missing from the JSON body (the body is absent or not a JSON object), THEN the system SHALL return `400 Bad Request`; an empty or whitespace value in an otherwise valid body SHALL return `204` under criterion 2.

### Requirement 5: Expose the endpoints through the gateway (FR-AUTH-004, FR-AUTH-005)

**User Story:** As a frontend developer, I want to call refresh and logout through the gateway without a bearer token, and have refresh protected from abuse, so that a client whose access token has expired can still renew it.

#### Acceptance Criteria

1. WHEN the gateway starts, THEN its configuration SHALL contain routes `auth-refresh` (`POST /api/auth/refresh`) and `auth-logout` (`POST /api/auth/logout`) to the `auth` cluster, each with `Order` 0, the `anonymous` authorization policy and the `RequestHeaderOriginalHost` transform, like `auth-login`.
2. WHERE the route's method is not `POST`, the system SHALL NOT match these routes (the request falls through to the existing authenticated `auth` catch-all, which answers `401` without a valid token).
3. WHEN a client sends more than 10 requests to `/api/auth/refresh` within one minute from the same client address, THEN the gateway SHALL answer the 11th with `429` and a `Retry-After` of 60, using a new `refresh` rate-limit policy: a sliding window of 1 minute with 6 segments and no queue, partitioned by the connection's remote address with forwarded headers ignored — the same mechanism as the `login` policy but with its own counter and a limit of 10, so that login and refresh attempts do not consume each other's permits.
4. WHERE `/api/auth/logout` is concerned, the gateway SHALL NOT apply a rate limit (see Known gaps).
5. WHEN the gateway tests read `appsettings.json`, THEN they SHALL assert the two new routes, their anonymous policy, the `refresh` policy on `auth-refresh` only, and that every other route and limit is unchanged.
6. WHERE a request reaches the Auth service directly (not through the gateway), the same two endpoints SHALL be anonymous, so the service's own tests do not depend on the gateway.
7. WHEN the OpenAPI document for Auth is generated, THEN it SHALL describe both endpoints, their request bodies and the `accessTokenExpiresAtUtc` field.

## Out of scope

- **Cookies** — the refresh token is only ever in the JSON body (the SPA is separately hosted); no `Set-Cookie`, no CSRF handling.
- **Session or device listing UI**, and a **revoke-all-sessions** endpoint — families make them possible later; none is built here.
- **Password change revoking tokens** — no password change exists (`User.ChangePassword` has no caller).
- **Sliding access tokens** — the access token keeps its fixed lifetime; clients renew it by refreshing.
- **Access-token revocation** — see Requirement 4.5.
- **Cross-tab coordination** — the frontend will serialize refreshes across tabs (`BroadcastChannel` plus `navigator.locks`); the server does not try to help (see Known gaps 1).
- **A shared rate-limit store** — the new policy is in-process like the others (accepted single-instance limitation, `api-gateway` spec).

## Known gaps

1. **A benign refresh race logs the user out.** Requirement 2.7 treats the loser of two simultaneous redemptions as a reuse, so its family is revoked and both tabs lose their session. This keeps reuse detection at full strength and follows `auth-identity`'s "no grace window" stance, and relies on the frontend serializing refreshes. The alternative is a short grace window or returning `401` to the loser without revoking, which tolerates two tabs but lets an attacker race the victim once without consequence.
2. **Expired and revoked rows are never purged.** Each login adds a row and each refresh adds a row; nothing deletes them in this feature. Add a scheduled cleanup (for example rows expired more than 30 days) in a later spec.
3. **Logout is not rate limited.** It needs a 64-random-byte secret and only ever revokes, so guessing it gains nothing; flooding it costs a hash and an indexed lookup. Revisit if it is abused.
4. **Reuse revokes by presentation, not by cause.** A token revoked by logout and then presented again also triggers the family revoke of Requirement 3.2; the family is already dead, so this is harmless, but it is not distinguished in the logs.
5. **`auth-identity` Requirements 6 and 7 and tasks 15-18 are now stale.** They should be replaced with a pointer to this spec when it is approved, and `docs/specs/README.md` should gain a row for this feature.
6. **The BRD has no refresh or logout flow.** `FR-AUTH-004` and `FR-AUTH-005` should be added to `docs/requirements.md` section 8, with the endpoints in section 13.
