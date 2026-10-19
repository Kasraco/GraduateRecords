# ADR-0001: Authentication model

- **Status:** Accepted
- **Date:** 2026-10-18
- **Deciders:** Project Owner
- **Related documents/ADRs:** AGENTS.md (security rules), future ADR on Roles/Permissions, future ADR on Student synchronization

## Context
The university operates a central SSO based on OAuth2/OIDC. After authentication,
its userinfo endpoint returns a flat key/value JSON in which the field `id`
contains the user's Iranian National ID (10 digits).

The application needs local control over users, roles, permissions and audit,
and both students and staff must sign in through the SSO. Each person must map
to exactly one local user. The university profile synchronization API is a
separate concern and its outage must not affect authentication.

## Decision

### Authentication source
1. Users authenticate through the university SSO using OAuth2/OIDC
   (Authorization Code flow; PKCE if supported). The code exchange happens
   server-side in the Web project.
2. Configured Authority: `https://idp.ajums.ac.ir` (config-driven).
   `https://sso.ajums.ac.ir` is also valid but not configured by default.
   Endpoints and issuer are read from OIDC discovery of the configured
   Authority, and the token issuer is validated against it. Accepting a second
   issuer requires a documented decision after verifying that both discovery
   documents return the same issuer.
3. Scope: `openid profile`. Add `email` only if a feature requires it.

### Local identity
4. ASP.NET Core Identity is the local user store and issues the application
   cookie. After a successful SSO callback, the app signs the user in with the
   Identity cookie. All authorization, roles, permissions and audit use the
   local user, not SSO tokens.
5. National ID rules:
   - Source: the `id` field of the userinfo response, mapped to `NationalId`
     in one dedicated claim-mapping component (Infrastructure).
   - Normalization: Persian/Arabic digits to Latin, trim whitespace, keep
     leading zeros, always `string`, never numeric.
   - Validation: exactly 10 digits and a valid National ID checksum.
   - Missing or invalid value: login is rejected, a generic user-friendly
     message is shown, and an audit event is written.
6. Identity `UserName` = normalized National ID. A separate `NationalId` column
   also exists with a unique index. A National ID maps to exactly one user;
   duplicate users must be impossible at the database level.
7. Users are matched only by National ID, never by email, name or phone.

### Roles and provisioning
8. Roles and permissions are managed only in the local database. SSO claims
   other than the National ID are never used to grant roles or permissions.
9. First-login provisioning is allowed only if the National ID exists in the
   synchronized local Student records, is found by a bounded first-provisioning
   directory lookup (ADR-0002 rule 3, AGENTS §12 — identity proofing is never
   skipped), or is pre-registered as staff/admin. Otherwise access is denied with
   a message such as "your account is not defined in this system", and an audit
   event is written. Having an SSO account alone never grants access. This
   lookup is a post-authentication provisioning step and is never part of the
   authentication path itself.
10. A person who is both a student and staff is one user with multiple roles.

### Local password and break-glass
11. Local password login is disabled for students and staff (no password hash).
12. A break-glass local administrator may exist for SSO outages. It is disabled
    by default, enabled only by configuration, requires a strong password and
    MFA, has lockout enabled, and every use is audited.

### Session and logout
13. Cookie: `HttpOnly`, `Secure`, `SameSite=Lax`, bounded lifetime with sliding
    expiration, redirect URIs registered per environment.
14. Logout clears the local cookie and then redirects to the SSO end-session
    endpoint (`https://idp.ajums.ac.ir/api/logout`, read from discovery when
    available).

### Separation from profile synchronization
15. Authentication never calls the student profile API. A previously
    synchronized student can sign in when the profile API is down. Sync
    metadata (`LastSyncedAt`, `LastSyncStatus`, `LastSyncError`) is independent
    of authentication.
16. SSO outage blocks new sign-ins only. Existing valid sessions continue.

### Secrets and logging
17. Client secret and any test credentials live only in user-secrets
    (development) or the server secret store (deployment). They are never in
    the repository, logs, documentation, or agent prompts.
18. Tokens, authorization codes and raw National IDs are not written to logs
    or audit payloads. National ID is masked in logs and UI where possible.

### Audit events (minimum)
Successful sign-in, failed sign-in (reason category only), invalid/missing
National ID, unknown user denied, user provisioned, break-glass login, logout.

## Alternatives considered
| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| SSO only, no local Identity | Simple | No local roles/permissions/audit linkage | Local authorization is a core requirement |
| Identity with local passwords only | Full control | Duplicate credentials, password management burden, ignores existing SSO | University SSO already exists |
| SSO + Identity keyed by email | Common pattern | Emails change and can collide | National ID is the stable unique key |
| SSO + Identity keyed by National ID (chosen) | One user per person, local authorization, simple | Depends on SSO returning a valid `id` | Chosen |

## Consequences
- Identity tables live in SQL Server via Infrastructure; the Web project hosts
  the OIDC handler and callback endpoints.
- A National ID normalization/validation component in Domain with unit tests is
  required (Persian digits, leading zeros, invalid checksum, wrong length).
- SSO outage blocks new logins; break-glass admin mitigates admin lockout.
- Configuration needed: Authority, ClientId, ClientSecret (secret store),
  redirect and post-logout URIs per environment.
- If the SSO changes the meaning of `id`, the validation in rule 5 rejects
  logins instead of silently creating wrong users.

## Verification before implementation
- Check `code_challenge_methods_supported` in discovery to decide whether PKCE
  is enabled.
- Confirm with a real userinfo response (values masked) that `id` is a
  10-digit National ID for both students and staff.

## Acceptance criteria
- Two sign-ins with the same National ID always resolve to the same user.
- Persian-digit National ID input normalizes to the same Latin-digit value.
- An invalid National ID from SSO is rejected and audited.
- A user not present in Student/staff records cannot access the system.
- Sign-in works while the university profile API is unavailable.
- No secret or token appears in logs, audit records, or source control.
