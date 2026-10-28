# ADR-0015: Dual authentication with SSO-first policy

- **Status:** Accepted
- **Date:** 2026-10-28
- **Deciders:** Project Owner
- **Supersedes:** ADR-0001 (Authentication model)
- **Amends:** ADR-0002 only where its SSO-only authentication assumptions conflict with this decision
- **Related documents/ADRs:** AGENTS.md, ARCHITECTURE.md, ADR-0002 (User provisioning, activation, verification and deactivation), future ADR on Roles/Permissions

## Context

The application originally adopted university SSO as the normal authentication mechanism for both students and staff, with a separate break-glass local administrator for exceptional SSO outages.

The application still requires university SSO as the primary authentication mechanism. However, relying exclusively on SSO for staff and administrators creates an operational dependency on the university identity provider. An SSO outage would prevent new staff and administrator sign-ins even though the GraduateRecords application, its local Identity database, and its authorization system might otherwise be healthy.

Staff and administrators therefore require a controlled local authentication path in addition to SSO.

Students do not require this fallback. Student identity is established through the university SSO and National ID claim, and introducing student passwords would create unnecessary credential-management and security responsibilities.

The application already uses ASP.NET Core Identity as its local user store. Roles, permissions, ownership, staff scope, account state, audit linkage, and application sessions are associated with the local `ApplicationUser`, not with the authentication mechanism.

The system must therefore support multiple authentication paths without creating multiple application identities for the same person and without allowing the authentication method itself to grant additional authorization.

## Decision

### 1. Authentication model

The application supports two formal authentication paths:

1. **University SSO**
   - OAuth2/OIDC Authorization Code flow.
   - PKCE is used when supported by the university identity provider.
   - SSO remains the primary and preferred authentication mechanism.

2. **Local credential authentication**
   - Implemented using ASP.NET Core Identity.
   - Uses National ID as the username and a locally managed password.
   - Available only to eligible Staff and Administrator accounts.
   - It is a first-class production authentication mechanism and is not a development workaround.

Break-glass authentication remains a separate emergency mechanism and must not be treated as ordinary local staff authentication.

### 2. Authentication matrix

| User type | University SSO | Local National ID + Password | Break-glass |
|---|---|---|---|
| Student | Yes | No | No |
| Staff | Yes | Yes | No |
| Administrator | Yes | Yes | No |
| Emergency Administrator | No | No | Yes |

Students are SSO-only.

Staff and Administrators may authenticate either through university SSO or through their active local credentials.

Break-glass credentials are reserved for exceptional administrative recovery and must not be used as a normal Staff or Administrator login mechanism.

### 3. Single local identity

All authentication methods resolve to the same local `ApplicationUser`.

`ApplicationUser.UserName` remains the normalized Iranian National ID.

The existing unique `NationalId` value remains the stable application identity key. A person must never receive separate local users merely because the person uses different authentication methods.

For Staff and Administrators:

- SSO authentication resolves the SSO National ID to the local `ApplicationUser`.
- Local authentication resolves the entered National ID to the same `ApplicationUser`.
- Roles, permissions, ownership, StaffScope, account state and audit identity are identical regardless of authentication path.

Authentication method must not create, duplicate, replace or implicitly elevate the user's application identity.

### 4. National ID rules

The existing National ID rules remain in force.

For SSO authentication:

- National ID is obtained from the configured SSO claim/userinfo mapping.
- Persian/Arabic digits are normalized to Latin digits.
- Whitespace is trimmed.
- Leading zeros are preserved.
- National ID is treated as a string and never as a numeric value.
- It must contain exactly 10 digits and pass the configured Iranian National ID validation.
- Invalid or missing National ID causes authentication to fail safely.

For local authentication, the entered National ID is normalized and validated using the same canonical normalization rules before user lookup.

Users are never matched by email, display name, phone number or another mutable profile attribute.

### 5. Authentication is independent from authorization

Successful authentication proves which local user is making the request. It does not determine what that user is allowed to do.

Roles, permissions, ownership checks, StaffScope and other authorization rules are evaluated from the local application state.

The authentication path must never implicitly grant a role, permission, broader StaffScope or ownership access.

A Staff or Administrator who signs in locally must receive the same authorization-ready application identity as when the same person signs in through SSO.

The authentication method may be recorded for audit and observability purposes but must not be used as an implicit authorization rule.

### 6. Student authentication

Students authenticate only through university SSO.

The application must not provide students with local passwords or a local password-login path.

Student provisioning, eligibility, synchronization and verification remain governed by ADR-0002 and the relevant synchronization decisions.

Having a valid university SSO account alone does not grant access to GraduateRecords.

### 7. Staff and Administrator local credentials

Staff and Administrator accounts may have local credentials.

There is no public or self-service registration.

Local Staff and Administrator accounts are created and managed only through authorized administrative workflows.

The administrative lifecycle must control, as applicable:

- account creation;
- activation and deactivation;
- role assignment;
- local-password availability;
- password initialization/reset;
- credential disabling;
- security-stamp rotation;
- auditing of security-sensitive account changes.

A local password must never be created merely because a Staff or Administrator signs in through SSO.

An Administrator must not be able to bypass existing authorization or last-active-administrator protections through the local-authentication workflow.

### 8. Password security

Local passwords use ASP.NET Core Identity's password hashing and verification mechanisms.

Production passwords must never be:

- hard-coded;
- committed to source control;
- stored in configuration as plaintext;
- logged or audited;
- shared between users;
- created as predictable defaults;
- seeded as known passwords.

Password hashes must never be exposed through logs, audit records, UI, API responses or agent prompts.

The existing password, lockout and rate-limiting security policy applies to local authentication.

Local authentication must fail with generic user-facing errors that do not reveal whether a National ID exists, whether the account has a local password, or which security check failed.

### 9. Account state and eligibility

Authentication does not bypass account-state rules.

A successful credential check does not by itself grant application access.

Existing account controls such as `IsActive`, `IsEligible`, `PhoneVerified` and applicable authorization policies remain effective according to ADR-0002 and subsequent accepted decisions.

A deactivated user must not regain access by switching from SSO to local authentication or vice versa.

Security-stamp invalidation continues to terminate affected existing sessions according to the application's security policy.

### 10. SSO outage behavior

An outage or configuration failure of the university SSO must not disable valid local authentication for eligible Staff and Administrators.

During an SSO outage:

- Students cannot establish new sessions.
- Staff with valid and enabled local credentials may sign in locally.
- Administrators with valid and enabled local credentials may sign in locally.
- Existing valid application sessions continue according to normal session rules.
- Break-glass remains available only according to its own emergency controls.

Local authentication must not call the university SSO and must not depend on SSO availability.

Failure of SSO must not cause the application to automatically weaken authorization, account-state, password, lockout or audit requirements.

### 11. Break-glass authentication

Break-glass authentication is distinct from ordinary local Staff/Administrator authentication.

It exists solely for exceptional administrative recovery.

It must:

- be disabled by default unless explicitly enabled according to configuration;
- be restricted to the emergency administrative identity;
- use strong credentials;
- use MFA when the approved implementation is available;
- use lockout and other applicable protections;
- be prominently and distinctly audited;
- never become a normal fallback account shared by administrators.

Ordinary Staff accounts must never use break-glass credentials.

Ordinary Administrator local login must not be recorded as break-glass authentication.

### 12. Provisioning and ADR-0002

ADR-0002 remains Accepted except where its SSO-only assumptions conflict with this ADR.

In particular, the following ADR-0002 decisions remain in force:

- no public self-registration;
- controlled Staff and Administrator provisioning;
- student just-in-time provisioning rules;
- Sama-based student eligibility;
- OTP verification rules;
- activation/deactivation rules;
- synchronization behavior;
- last-active-Administrator protection;
- audit requirements;
- retention rules.

Statements in ADR-0002 that require Staff or Administrators to authenticate exclusively through SSO are superseded by this ADR.

Statements that assume the first Administrator can never possess local credentials are likewise superseded.

Student SSO provisioning flows remain unchanged unless another Accepted ADR explicitly changes them.

### 13. Development authentication

Any temporary development-only authentication mechanism exists solely to enable development before real SSO configuration is available.

Development authentication is not a third production authentication method.

Once formal local authentication is implemented and provides the required development access, the temporary `/dev/login` mechanism must be removed.

Development convenience must never weaken Production, Staging or Testing authentication requirements.

No development endpoint may silently become a production fallback.

### 14. OIDC configuration and environment behavior

University SSO configuration remains external and secret-store driven.

No fake, placeholder, hard-coded or synthetic production OIDC credentials are permitted.

Production, Staging and Testing must fail closed when required SSO configuration is missing or invalid according to the application's environment configuration policy.

Development may run without real SSO configuration, provided that:

- no fake OIDC provider configuration is introduced;
- the absence of SSO is clearly represented;
- protected application functionality still requires authentication;
- formal local authentication may be used only by eligible local Staff/Administrator accounts.

### 15. Session behavior

Both SSO and local authentication ultimately issue the application's ASP.NET Core Identity authentication cookie.

The existing application-cookie security requirements remain in force, including:

- `HttpOnly`;
- `Secure`;
- `SameSite=Lax`;
- bounded lifetime;
- sliding expiration according to configured policy;
- absolute session lifetime according to configured policy;
- Security Stamp revalidation.

SSO logout additionally performs the configured identity-provider end-session/logout behavior when applicable.

Local logout clears the local application session and does not require an SSO end-session operation.

### 16. Audit and observability

Authentication events must identify the authentication path when technically available.

At minimum, the system must distinguish:

- University SSO authentication;
- Local credential authentication;
- Break-glass authentication.

Relevant events include successful authentication, failed authentication, lockout, logout, credential-management actions and break-glass use.

Authentication audit events use the existing audit infrastructure and privacy rules.

Passwords, password hashes, authorization codes, access/refresh/identity tokens, client secrets and other authentication secrets must never be written to application logs or audit records.

Full National IDs must not be exposed in audit/log payloads where existing masking rules require masking.

Authentication method is an audit/observability attribute and must not be treated as an implicit authorization rule.

## Alternatives considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| SSO-only for everyone | Simple credential model; centralized authentication | SSO outage prevents new Staff/Admin sign-ins | Operational dependency is too strong |
| Local passwords for everyone | Independent of SSO | Creates unnecessary student credential lifecycle and duplicates university authentication | Students remain SSO-only |
| Separate local username for Staff/Admin | Familiar local-login model | Introduces another identity key and account-linking complexity | National ID already uniquely identifies the local user |
| Shared emergency account for all SSO outages | Easy operational workaround | Poor accountability, shared credentials, excessive privilege | Break-glass must remain exceptional |
| SSO-first + local credentials for Staff/Admin | Preserves university SSO while providing operational resilience | Requires secure password lifecycle and additional authentication UI | Chosen |

## Consequences

- University SSO remains the primary authentication mechanism.
- Students remain dependent on SSO for new authentication sessions.
- Staff and Administrators can continue to authenticate when SSO is unavailable if they have valid local credentials.
- ASP.NET Core Identity must support local password credentials for eligible Staff and Administrator users.
- `ApplicationUser.UserName` remains the normalized National ID.
- No separate local username identity is introduced.
- Local credential lifecycle requires controlled administrative UI/use cases.
- Authentication UI must clearly distinguish university SSO from Staff/Administrator local login.
- Audit events must distinguish SSO, local and break-glass authentication.
- Temporary `/dev/login` support must be removed after formal local authentication is available.
- Authorization behavior remains independent from authentication method.
- ADR-0001 remains in the repository as historical documentation but is superseded by this ADR.
- ADR-0002 remains Accepted except for the SSO-only assumptions explicitly amended by this ADR.

## Security implications

Supporting local passwords increases the application's responsibility for credential security.

Therefore:

- local authentication is limited to Staff and Administrators;
- ASP.NET Core Identity password hashing is mandatory;
- lockout and rate limiting are mandatory;
- password reset and initialization must be controlled administrative operations;
- enumeration-resistant login errors are required;
- authentication secrets must never be logged or audited;
- switching authentication method must never bypass deactivation, eligibility or authorization controls;
- break-glass remains isolated from ordinary local authentication.

## Verification before implementation

Before implementing or modifying authentication code:

1. Confirm that `ApplicationUser.UserName` and `NationalId` remain uniquely mapped to the same person.
2. Confirm that existing Identity schema can support passwords without introducing a second user record.
3. Review ADR-0002 flows affected by Staff/Admin local credentials.
4. Verify existing password, lockout, cookie, rate-limit and Security Stamp settings.
5. Identify all temporary `/dev/login` code and tests that must be removed or replaced.
6. Identify all existing authentication audit events that require an authentication-method attribute.
7. Verify that local authentication can operate without contacting the university SSO.
8. Verify that Student accounts cannot use the local-password authentication path.

## Acceptance criteria

- A Student can authenticate through university SSO but cannot authenticate with a local password.
- A Staff user can authenticate through SSO and local National ID + password when local credentials are enabled.
- An Administrator can authenticate through SSO and local National ID + password when local credentials are enabled.
- SSO and local authentication for the same person resolve to exactly the same `ApplicationUser`.
- Authentication method does not change Roles, Permissions, StaffScope, ownership or account-state enforcement.
- A deactivated user cannot regain access by switching authentication methods.
- A Staff/Administrator with valid local credentials can authenticate while the university SSO is unavailable.
- Local authentication does not contact the university SSO.
- No public/self-service registration exists.
- No default, shared, seeded, hard-coded or predictable production password is introduced.
- Student accounts do not receive local passwords.
- Break-glass authentication remains separate from ordinary Administrator local authentication.
- Authentication audit events distinguish SSO, local and break-glass when technically available.
- Passwords, password hashes, tokens and authentication secrets never appear in logs or audit records.
- Temporary `/dev/login` authentication is removed after formal local authentication is available.
- Existing ADR-0002 provisioning, eligibility, OTP, deactivation and retention rules remain effective except where explicitly amended by this ADR.