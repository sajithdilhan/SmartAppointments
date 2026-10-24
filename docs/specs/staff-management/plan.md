# Staff management — Plan

**Status:** Implemented (2026-10-24)

## Problem and goal

Admins can create a staff account (`POST /api/auth/staff`) and move one to another branch (`PUT /api/auth/staff/{id}/branch`), and nothing more. They cannot list the staff accounts that exist, so the web client's admin console cannot show a staff table, and moving someone needs their user id from the database. Nor can an admin take an account out of service: a staff member who leaves keeps a working login.

Once this feature exists, an admin can:
- List the staff accounts, filtered by branch and active state and paged, and read one by id.
- Deactivate and reactivate an account.
- Delete an account.

Each of the last three takes effect on the account's sessions at once.

## Scope

All in the **Auth** service, on the existing admin-only `StaffController` (`401` without a token, `403` for Staff and Customer callers, as today).

- **List**: `GET /api/auth/staff?branchId=&isActive=&page=&pageSize=`
  - Only users with role `Staff` that are not deleted, as `{ items, page, pageSize, totalCount }` (the paging shape of Booking's `/my`, `PagedResponse<T>`).
  - Optional `branchId` (not checked against Availability; an unknown branch gives an empty list) and `isActive` (`true` or `false`; both when absent) filters, combined with AND.
  - `page` defaults to 1, `pageSize` to 20 (at most 100). Bad values of any of the four parameters give one `400` listing every failure, like `/my`: the action binds all four as raw strings, parsed by one helper shared by the validator and the handler (Booking's `MyAppointmentsParameters` pattern), because typed binding would answer `isActive=maybe` or `branchId=xyz` with a separate model-binding `400` before the validator runs.
  - A stable sort order, so pages do not shift between requests (proposed: `RegistrationDateUtc`, then `Id`).
  - Each item carries `isActive` and `registeredAtUtc`.
- **Read one**: `GET /api/auth/staff/{id}` returns `200` with the staff shape of the list items. It is `404` for an unknown id, a non-staff user or a deleted account. Create then returns `201` with a `Location` header pointing at it (it has none today).
- **Deactivate**: `POST /api/auth/staff/{id}/deactivate`. It sets `IsActive = false` and revokes every refresh-token family of the user in the same transaction. It returns `204`, and a repeat is `204` (the branch activate/deactivate convention). A repeat still revokes any live family left (one cheap conditional update), so a login that raced the first call is cleaned up. Login and refresh already refuse inactive users.
- **Reactivate**: `POST /api/auth/staff/{id}/activate`. It checks the staff member's branch through Availability, as create and move do: `422` if the branch is unknown or inactive, `503` if Availability is down, and the account stays inactive on either error. Then it sets `IsActive = true` and returns `204`; a repeat is `204`. No sessions come back: the staff member signs in again.
- **Delete**: `DELETE /api/auth/staff/{id}`, a **soft delete**. It sets `DeletedAtUtc` (and `IsActive = false`), revokes every refresh-token family in the same transaction, and returns `204`.
  - A deleted account can never sign in again (login and refresh refuse it because it is inactive). It is absent from the list, and every staff endpoint treats its id as unknown (`404`). Where else it shows is open decision 1.
  - The row stays, so a future audit ("completed by") can still resolve the name. The email stays registered and cannot be reused.
  - An active account may be deleted directly: deactivating first is not required.
- `404` on all three for an unknown id, a non-staff user or a deleted account. Move (`PUT .../branch`) also gets the deleted-account `404`.
- **Domain and persistence**: `User.DeletedAtUtc` with a domain method that marks the account deleted (and inactive); a migration adding the column. Saving a status change and revoking the user's sessions in one transaction generalises the existing `IUserRepository.SaveBranchMoveAsync`.
- **BRD**: widen FR-AUTH-006 to "create, list, move, deactivate, reactivate and delete staff", and add the endpoints to the Auth endpoint draft in `docs/requirements.md`.
- Tests in `Auth.Tests`; requests in `Auth.Api.http` and the gateway `.http`; `CLAUDE.md` and `docs/specs/README.md`.

## Out of scope

- Undeleting an account, or purging deleted rows. Delete is final from the API.
- Editing a staff member's name, email or phone, and resetting their password.
- Deactivating, deleting or listing customers or admins, or a general user search.
- Cutting off an access token already issued. A deactivated, deleted or moved staff member keeps their current access token until it expires (at most 60 minutes), the same accepted residual risk as ADR-0007.
- Branch names in the list: the web client already reads `/api/branches` and joins them itself, so Auth gains no new call to Availability.
- The web client's staff table and actions: the frontend's F7 phase, in `smart-appointments-web`.
- Gateway changes: `/api/auth/{**catch-all}` already routes every new path (any method) and none of them is rate-limited.

## FR-IDs

| FR-ID | Summary |
|---|---|
| FR-AUTH-006 | Manage Staff. Today "create and move"; this feature adds list, deactivate, reactivate and delete, and the BRD row is widened to say so |
| FR-AUTH-003 | Role-based access: every staff-management endpoint is admin-only |

## Approach

- **List**: a MediatR query `ListStaffQuery(BranchId?, IsActive?, Page, PageSize)` with a validator. Its handler calls a new `IUserRepository.ListStaffAsync`, which returns the page and the total count. The controller binds the query string into a parameters record and returns `this.ToActionResult(...)` on failure, as Booking's `ListMyAppointments` does.
  - The page type is a small Auth-local record mirroring Booking's `PagedResponse<T>`. Sharing it through BuildingBlocks would need an RFC that this feature does not warrant, and the duplication is one record.
- **Lifecycle commands**: `SetStaffActiveCommand(Id, IsActive)` (one handler for activate and deactivate, like Availability's `SetBranchActiveCommand`) and `DeleteStaffCommand(Id)`.
  - Reactivation reuses the existing `StaffBranchCheck` against Availability.
  - Deactivation and deletion save the user and revoke the user's refresh tokens in one transaction. This is the mechanism `SaveBranchMoveAsync` already has, generalised to "save this user's change and end all their sessions".
- **Excluding deleted accounts**: explicit checks, not an EF Core global query filter. Every staff handler (get, move, activate, deactivate, delete) returns `404` when the user is deleted, and the list query has a `DeletedAtUtc IS NULL` predicate. Login and refresh refuse deleted accounts because they are also inactive. A global filter is unsuitable: it would hide a deleted email from the duplicate check in register and create-staff, so the unique `Users.Email` index would reject the insert only through the race path. It would also hide deleted users from the internal lookup, which open decision 1 keeps.
- **One transactional save**: `SaveBranchMoveAsync` (`SaveChangesAsync` plus an `ExecuteUpdate` revoking live tokens, in one transaction) is generalised and renamed, for example `SaveWithSessionRevocationAsync`. Move, deactivate and delete all use it, and the existing move tests keep passing against the new name.
- **Item shape**: each item reuses `StaffResponse`, extended with `isActive` and `registeredAtUtc` (mapped from `User.RegistrationDateUtc`), so the list, the single read and the create and move responses keep one shape.
- **Read one**: a `GetStaffQuery(Id)` and handler over the existing `GetByIdAsync`, returning `404` unless the user is staff and not deleted. Create's action switches to `CreatedAtAction` naming the new get action, so `Location` carries the gateway's host through the existing `RequestHeaderOriginalHost` transform. The "No staff GET exists" comment goes, and the create controller test changes from asserting a status-code result to `CreatedAtActionResult`.
- **Queries**: count and page as two queries, as Booking does. No `(Role, BranchId)` index for now: staff are tens of rows, so it can come later with no API change.

## Affected services and projects

| Service / project | Change |
|---|---|
| Auth.Domain | `User.DeletedAtUtc`, a delete method (also deactivates); `Activate`/`Deactivate` exist already |
| Auth.Application | List query, validator, handler, models; `SetStaffActiveCommand`, `DeleteStaffCommand`, handlers; `StaffResponse` fields; `IUserRepository` list and save-with-revocation members |
| Auth.Infrastructure | `UserRepository` queries and the transactional save; migration (`DeletedAtUtc`, possibly an index) |
| Auth.Api | Five actions on `StaffController` (list, get, activate, deactivate, delete), create's `Location`; `Auth.Api.http` |
| Auth.Tests | Domain, validator, handler and controller tests |
| `docs/requirements.md`, `CLAUDE.md`, `docs/specs/README.md`, gateway `.http` | FR-AUTH-006 wording, endpoint drafts, status, requests |
| ApiGateway, Availability, Booking, BuildingBlocks | None. Booking stores no staff ids (complete and no-show record no actor), so deletion leaves no dangling reference there |

## Risks

- **Access token outlives the change**: up to 60 minutes after deactivation, deletion or a move, the staff member can still call Booking with their branch (list, complete, no-show). Accepted, as ADR-0007. Sessions are revoked, so no new access token can be obtained.
- **Missed deleted-account check**: with explicit checks rather than a global filter, an endpoint that forgets one would act on a deleted user. Mitigated by a handler test per endpoint for the deleted case.
- **Login racing a deactivation**: login checks `IsActive` before saving its new token family, with no concurrency token, so a login that started just before the deactivation can store a fresh family afterwards. Refresh refuses an inactive user and revokes that family, and a repeat deactivate revokes it too, so the impact is the same access-token window as below. Accepted.
- **Personal data exposure**: the list returns staff emails and phones. Mitigated by the admin-only policy, with tests for `403` and `401`.
- **Changing `StaffResponse`**: create and move bodies gain fields too. The change is additive, so existing clients are unaffected; the frontend regenerates its types in F7.
- **Soft-deleted email stays taken**: re-hiring someone with the same address needs a new email. Accepted, the price of the audit trail.
- **Migration**: one nullable column, safe to roll forward. Old code rolled back over deleted rows ignores `DeletedAtUtc`, so they look like ordinary inactive accounts. They still cannot sign in, and old code has no activate endpoint to revive them.

## Open decisions

Answered on 2026-10-24:
- Delete is a **soft delete** (`DeletedAtUtc`), hidden from the staff endpoints and profile lookup (see decision 1 below), email stays taken.
- Deactivation and deletion **revoke all sessions** in the same transaction.
- Reactivation **checks the branch** (`422` inactive or unknown, `503` Availability down).
- The list **includes deactivated accounts**, with `isActive` on each item and an `isActive` filter.

- Each staff item also carries **`registeredAtUtc`** (not `lastLoginAtUtc`).
- **No text search** in the list for now. A `search` parameter can be added later without breaking anything.
- **`GET /api/auth/staff/{id}` is added now**, and create returns a `Location` header pointing at it.

Raised by the plan review (2026-10-24):

1. **Where a deleted account still shows outside the staff endpoints**:
   - `GET /api/auth/profile?email=` (Admin and Staff can look anyone up) finds it through `GetByEmailAsync`.
   - Booking's internal lookup `POST /internal/users/lookup` returns users of every role through `ListByIdsAsync`.
   - `GET /api/auth/me` works on the deleted member's still-valid access token.

   Recommended: profile-by-email gives `404` for a deleted account, and the internal lookup keeps returning it, so future audit names resolve. `/me` falls under the accepted access-token window. Answer (2026-10-24): **as recommended**.
2. **Moving a deactivated staff member to another branch**: options:
   - A. Allow it. The branch is only data, and the account stays inactive.
   - B. `409`.

   Recommended: A, so an admin can move someone to an active branch and then reactivate them. That is the way out of the reactivation `422` when the old branch has closed. Answer (2026-10-24): **A, allow it**.

Settled while writing requirements (2026-10-24): a repeat `DELETE` is **`404`**, since the account is then unknown. The sort order is registration instant, then `id`.

## ADRs expected

- **Soft-delete staff accounts.** A real choice against hard delete, and hard to reverse once audit data relies on it.
- **End all sessions when a staff account is deactivated or deleted.** It extends ADR-0007's reassignment rule to two more state changes; ADR-0007 itself stays unedited.

Both are written as Proposed during design.

## RFC

**Required:** No. Only the Auth service changes. The new endpoints add to Auth's own public API without changing any inter-service contract, BuildingBlocks is not touched, and the gateway already routes the paths.

## Work split

| Chunk | Owns (projects / folders) | Depends on |
|---|---|---|
| 1. Lifecycle | `Auth.Domain` (`DeletedAtUtc`, delete method), migration, `IUserRepository`/`UserRepository` generalised transactional save, activate/deactivate/delete commands and handlers, the three controller actions, the move handler's switch to the generalised save and its deleted (and decision 2) checks, `StaffResponse` fields, tests | — |
| 2. Reads | List query, parameter parser, validator, handler, models, `ListStaffAsync`; `GetStaffQuery` and handler; the list and get actions, create's `Location`; the decision 1 changes to profile lookup; `Auth.Api.http`; tests | 1 (same controller, repository, `StaffResponse` and `DeletedAtUtc`) |
| 3. Docs (orchestrator) | `docs/requirements.md`, `CLAUDE.md`, `docs/specs/README.md`, gateway `.http` | 1, 2 |

Chunks 1 and 2 share `StaffController` and the repository, so they run one after the other, not in parallel.

## Verification plan

**Automated tests:** in `Auth.Tests`, in the xUnit + Moq style:
- **Domain:** delete marks the account deleted and inactive; `Activate`/`Deactivate` as today.
- **Validators:** list paging bounds and filters, several failures reported together, including unparsable values (`isActive=x&branchId=xyz&page=0` gives one `400` naming all three).
- **Registering a deleted staff member's email** gives `400` "already registered" (handler test with the repository returning a deleted user).
- **Handlers:**
  - Deactivate and delete call the save-with-revocation path; a repeat deactivate changes nothing.
  - Reactivate makes a branch check: `422` or `503` leaves the account inactive.
  - All of them return `404` for an unknown, non-staff or deleted id, and move does too for a deleted id.
  - List passes its filters and paging to the repository and maps the page.
  - Get returns the staff item, and `404` for an unknown, non-staff or deleted id.
- **Controllers:** `204`, `200` and the problem body for each action; create's `201` carries a `Location` naming the get action; the `AdminPolicy` attribute covers the new actions.
- **Repository:** the queries are covered by the manual check, since the project has no database-backed tests yet.

**Manual end-to-end checks** (Docker Compose, through the gateway on `localhost:5290`):
- **List:**
  - Admin `GET /api/auth/staff` lists `staff.test@example.com` and no customers or admins.
  - The `branchId` and `isActive` filters narrow the list.
  - `?pageSize=1&page=2` returns the second staff member with the same `totalCount`.
  - `?page=0&pageSize=500` gives one `400` naming both failures.
- **Read one:**
  - Creating a staff member returns a `Location` on the gateway's host; following it gives `200` with the same body.
  - A customer's id gives `404`.
- **Deactivate:**
  - Deactivate the staff member: `204`. Their refresh token now gives `401` and login gives `401`, while their current access token still lists the branch's appointments (the accepted window).
  - The list shows `isActive: false`, and a repeat deactivate gives `204`.
- **Reactivate:**
  - Reactivating gives `204`, and login works again.
  - Reactivating while the branch is deactivated in Availability gives `422`, and the account stays inactive.
- **Delete:**
  - Delete gives `204`. The account is gone from the list and login gives `401`.
  - Move, activate and deactivate on its id give `404`.
  - Registering its email again gives `400` "already registered".
- **Access:** a Staff or Customer token on every endpoint gives `403`, and no token gives `401`.
