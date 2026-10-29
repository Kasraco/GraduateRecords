# ADR-0016: Bootstrap Administrator provisioning and local credential lifecycle

- **Status:** Accepted
- **Date:** 2026-10-29
- **Deciders:** Project Owner
- **Related documents/ADRs:** AGENTS.md §13 (authentication and authorization), §14 (audit), §17 (seeds/migrations), §19 (exactly two hosted services), §24 (ADR schedule) · ARCHITECTURE.md §10 (authorization), §11 (audit), §15 (security boundaries) · ROADMAP.md Phase 1A · ADR-0002 (provisioning/activation — rule 2.2 is operationalized here), ADR-0004 (staff data scope — roles; last-Administrator and delegation semantics are deferred to ADR-0017), ADR-0015 (dual authentication — complemented, not superseded), ADR-0006 (environments and secrets), ADR-0008 (security stamp and session rules)

## Context

The system has no supported way to create its first Administrator. Local users are
created only by an Administrator or by just-in-time student provisioning
(ADR-0002 §1.2), which is circular for the first Administrator. The UC2 inspection
confirmed that no seed, configuration mechanism, admin UI, CLI tool or password
initialization workflow exists in the codebase, and that no production code path
can set `UserKind = Administrator`.

Facts and constraints supplied by the accepted documents:

- ADR-0002 §2.2 already decides: "The first Administrator is seeded from
  configuration (National ID and mobile number only; no passwords or secrets).
  The seeded user still signs in through SSO and passes OTP verification."
- ADR-0015 §12 superseded the assumption that the first Administrator can never
  possess local credentials, and ADR-0015 §7 requires password
  initialization/reset to be controlled administrative operations — but neither
  ADR defines how the *first* local credential comes into existence without an
  existing Administrator to manage it (the chicken-and-egg problem).
- AGENTS §13.4: no default, shared, seeded, guessable or hard-coded production
  passwords; local lifecycle restricted to authorized administrative
  functionality.
- AGENTS §13.7 / ADR-0015 §13: `/dev/login` is removed; a development
  authentication mechanism must never become a production mechanism or a
  production fallback.
- AGENTS §13.8 / ADR-0015 §14: Development may run without OIDC configuration;
  no fake OIDC credentials may be introduced.
- ADR-0015 §5 / AGENTS §13.3: authentication method never determines
  authorization.
- Current implementation: `UserKind` (`LocalUserKind`) decides only local-login
  eligibility (`LocalSignInHandler` rejects `Student`); no roles, permissions or
  `PermissionPolicyProvider` exist yet (UC2, pending); `AccountController`
  enforces `SafeReturnUrl`, enumeration-resistant local login and per-request
  security-stamp validation.
- Audit reality: Phase 1A writes audit through the best-effort Serilog sink;
  the append-only transactional `AuditLog` arrives in Phase 1C (ROADMAP). The
  inspection flagged this sequencing gap; this ADR must not paper over it.
- ADR-0002 access-state controls (`IsActive`, `IsEligible`, `PhoneVerified`)
  are not implemented yet; this ADR neither implements nor waives them.

Known unknowns deliberately left to other decisions: role and permission naming
(`GraduateStaff` vs `Staff`, lowercase-dot vs PascalCase), Student permission
mapping, permission delegation semantics, general `UserKind` ↔ Role consistency,
the general last-Administrator invariant (all → future ADR on Roles/Permissions
administration, expected as ADR-0017), and the UX/token mechanism for
administrative credential reset of Staff accounts.

## Decision

### 1. One bootstrap mechanism

1.1 The application has exactly one special bootstrap mechanism: **Bootstrap
    Administrator provisioning**. Its sole purpose is to establish the first
    normal Administrator account. There is no second bootstrap path, no
    emergency seeding path and no supported manual-database path.
1.2 Bootstrap operates on the normal `ApplicationUser` through the normal
    Identity store. It must NOT create a special `BootstrapUser` type, a
    system account, a shared account or any second user record.
1.3 Bootstrap runs once per application start, invoked from the composition
    root (`Program.cs`) after the database schema is in place. It is a
    config-gated, idempotent reconciliation step — NOT a hosted service
    (AGENTS §19 allows exactly two), NOT a background job, NOT an EF Core
    migration, and NOT a schema change.
1.4 Provisioning at startup requires no authentication and creates no session;
    the rules in §5 govern everything that follows.

### 2. Bootstrap configuration

2.1 The bootstrap identity is supplied through configuration:

    | Key | Content | Rules |
    |---|---|---|
    | `BootstrapAdministrator:NationalId` | Normalized 10-digit National ID | Validated with the standard Domain `NationalId` rules (length, checksum, not all-same digits) |
    | `BootstrapAdministrator:MobileNumber` | Mobile number, normalized to `09xxxxxxxxx` | Preserved from ADR-0002 §2.2 ("National ID and mobile number only") |

2.2 Configuration MUST contain identity information only. It MUST NOT contain:
    password, password hash, temporary password, default password, shared
    password, OIDC tokens, client secrets, one-time login credentials, or any
    other authentication material.
2.3 Both keys are empty by default in the repository. Actual values are
    environment-specific and supplied outside source control (environment
    variables per ADR-0006 §4.1, or user-secrets in Development) — consistent
    with AGENTS §18 and ADR-0002 §2.2 ("no passwords or secrets").
2.4 Empty/missing values are legitimate **once an Administrator role holder
    exists** (bootstrap completed; configuration consumed, not authoritative —
    §4.3). While **no Administrator role holder exists**, absent configuration
    is fatal outside Development: startup MUST fail closed with a clear
    configuration/operational error and no user/account mutation (§14.2);
    Development may start without it for inspection/testing only (§8, §14.2).
    Present-but-invalid values are an operator error and fail startup
    validation in every environment (`ValidateOnStart` convention, AGENTS §18).
2.5 Display name and other profile fields are not part of bootstrap
    configuration (ADR-0002 §2.2 lists only National ID and mobile number).
    Display-name population is display-only and does not affect authentication,
    authorization or this ADR.

### 3. Bootstrap outcome: category and authorization are separate

3.1 The required bootstrap outcome is exactly:

    ```text
    ApplicationUser
        NationalId  = configured bootstrap identity
        PhoneNumber = configured mobile number
        UserKind    = Administrator

    Authorization:
        Administrator role   (assigned through the UC2 role infrastructure)
    ```

3.2 **UserKind is authentication eligibility / account category only.**
    `UserKind = Administrator` makes the account eligible for local password
    login (ADR-0015 §2); it MUST NOT itself grant Administrator authorization,
    and no code path may ever use `UserKind` as an authorization check
    (AGENTS §13.3, ADR-0015 §5).
3.3 **Role + Permission is authorization.** Administrator authorization comes
    exclusively from the Administrator role and its permission mapping through
    the normal role/permission system (ARCHITECTURE §10, ADR-0004 §2).
3.4 Both mechanisms must be established through their respective mechanisms;
    neither substitutes for the other.

### 4. Idempotency and completion semantics

4.1 Bootstrap is **idempotent**. Repeated application startup MUST NOT:
    create duplicate users; create duplicate role assignments; reset
    credentials; rotate the SecurityStamp unnecessarily; overwrite unrelated
    profile changes (an existing `PhoneNumber` or name is never clobbered); or
    continuously elevate any user after bootstrap is complete.
4.2 **Completion guard (bootstrap-specific).** Bootstrap performs any work
    only while **no Administrator role holder has yet been established**.
    While any account holds the Administrator role, startup bootstrap is
    completely inert: no writes of any kind, whatever the configuration says.
    This role-holder test is used **only** to decide whether automatic
    bootstrap processing has completed. It MUST NOT be read as, or silently
    define, the general "last active Administrator" invariant: the general
    definition of an active/effective Administrator, last-Administrator
    protection, concurrency behavior, role removal/demotion protection and
    interaction with account activation state remain deferred to ADR-0017
    (§11).
4.3 **Bootstrap completion semantics.** Bootstrap is complete once the
    configured identity has been provisioned (`UserKind = Administrator`) and
    the Administrator role has been established on it. From that point the
    bootstrap configuration is **consumed, not authoritative**:
    - changing `BootstrapAdministrator:NationalId` later MUST NOT transfer
      Administrator privilege to another person — the completion guard (4.2)
      keeps bootstrap inert as long as any Administrator role assignment
      exists;
    - deleting or blanking the configuration after completion MUST NOT remove,
      demote or alter the established Administrator;
    - bootstrap configuration is never the source of truth for permanent
      authorization after completion — roles (and, for eligibility, UserKind)
      are, managed through authorized administrative functionality.
4.4 Changing the bootstrap identity after completion, if ever needed, is an
    **explicit administrative/recovery operation**, never normal startup
    reconciliation. Any such recovery procedure that demotes or removes the
    bootstrap Administrator MUST disable or correct the bootstrap
    configuration *first*, so startup reconciliation cannot re-grant what an
    operator deliberately removed.
4.5 **Resume vs refuse.** When the completion guard does not apply:
    - configured identity has no user → create the user and assign the role;
    - configured identity exists as `UserKind = Administrator` without the role
      (a partial earlier run) → assign the missing role only — idempotent
      resume of bootstrap's own outcome;
    - anything else (see §14) → refuse with no writes.
4.6 Bootstrap never deletes, demotes or "repairs" data it did not create. There
    is no destructive reconciliation.

### 5. Provisioning ≠ authentication ≠ authorization ≠ credential initialization

| Concept | Meaning | Bootstrap's role |
|---|---|---|
| **Provisioning** | Creating/reconciling the local `ApplicationUser` and its authorization state (§3) | Performed by bootstrap |
| **Authentication** | Proving who the person is (University SSO primary; local credential per ADR-0015) | **Never performed, manufactured or bypassed by bootstrap** |
| **Authorization** | Roles, permissions, StaffScope, ownership (UC2 model) | Bootstrap may *assign the Administrator role*; it never grants anything through session state or UserKind |
| **Credential initialization** | Establishing the first local password (§6) | Separate, explicit, authenticated operation |

5.1 The bootstrap process MUST NOT create or manufacture an authenticated
    session, MUST NOT bypass, shorten or skip OIDC validation, MUST NOT sign
    anyone in automatically, and MUST NOT recreate `/dev/login` or any
    equivalent endpoint.
5.2 The provisioned person still proves identity through an approved
    authentication path (University SSO is primary) before any session exists
    and before the credential-initialization operation (§6) can be reached.
5.3 Being listed in the bootstrap configuration is not authentication: it
    proves nothing about a request. Only successful authentication does.

### 6. First local credential: SSO-authenticated self-initialization

6.1 **Decision:** after the bootstrap Administrator has successfully
    authenticated **through University SSO**, that same authenticated account
    may initialize its own local credential through a dedicated, narrowly
    scoped **credential-initialization operation**.
6.2 This is NOT general self-service password registration. It never creates
    accounts, never resets an existing credential and never targets another
    account. It is permitted only when **all** of the following are true:
    - the caller is authenticated;
    - the target is the caller's own `ApplicationUser` (self only);
    - the account is eligible for local authentication (`UserKind` is Staff or
      Administrator, ADR-0015 §2);
    - the account has no initialized local credential (no password hash set);
    - the account satisfies the required Administrator authorization check
      (holds the Administrator role; depends on UC2, see §10);
    - the account passes all account-state and security checks (ADR-0002
      access-state rules and ADR-0015 §9 where implemented; antiforgery;
      rate limiting; lockout policy).
6.3 The new password is entered by the authenticated user and processed only
    through ASP.NET Core Identity password APIs (`UserManager`, policy from
    `IdentityOptionsSetup`: minimum 14 characters). The plaintext password
    never leaves normal request processing: never logged, never in audit
    metadata, never stored in configuration, never generated by the server,
    never shared, never echoed back.
6.4 After successful initialization, the SecurityStamp is rotated where the
    selected Identity operation/security model requires it, so any earlier
    session state is invalidated per ADR-0008 §5.3.
6.5 The operation is exposed through the Web layer with the same protections
    as other credential endpoints (antiforgery, rate limiting, generic failure
    messages). The Identity password call belongs in `AccountController`
    (AGENTS §2.2 allows password change/reset plumbing there); eligibility and
    authorization decisions live in an Application handler, mirroring the
    `LocalSignInHandler` split.
6.6 The operation writes one audit event (`Credential.LocalCredentialInitialized`)
    without any credential material (§12).
6.7 **Scope note:** because the operation requires the Administrator role, it
    solves the bootstrap chicken-and-egg and gives later-created
    Administrators a credential path without tokens. **Staff first-credential
    initialization is not granted by this ADR** — see §7.

### 7. Subsequent Staff/Administrator credential lifecycle

After the first Administrator exists, Staff and Administrator accounts are
created and managed through authorized administrative functionality
(ADR-0015 §7, AGENTS §13.4). This ADR fixes principles only and deliberately
does NOT design an account-management subsystem:

- Students remain SSO-only; no local credential path for Student accounts
  (ADR-0015 §6).
- Local credentials are allowed only for Staff and Administrator account
  categories (ADR-0015 §2).
- No public/self-service registration (ADR-0002 §1.1).
- No administrator-visible existing passwords; no password retrieval; no
  password stored in application configuration; no default or shared
  passwords.
- Password hashing exclusively through ASP.NET Core Identity.
- Credential operations (initialization, reset, disabling, security-stamp
  rotation) each require explicit authorization and are audited without
  credential material (§12).
- Credential changes never alter Roles, Permissions, StaffScope or ownership
  rules, and the authentication method never changes authorization
  (ADR-0015 §5, AGENTS §13.3).
- **Deferred:** the concrete UX/token mechanism for administrative
  credential initialization/reset applied by an administrator to another
  account (notably the first Staff password) requires a later decision before
  that flow ships. No temporary, generated or administrator-chosen password is
  used as a stopgap.

### 8. Development without OIDC

8.1 **Development bootstrap may provision identity but never bypasses
    authentication.** When OIDC is unconfigured or unavailable, the
    application starts and shows the login/unavailable state (existing
    `AccountController.Login` behavior); the bootstrap Administrator may be
    provisioned if configuration says so, but is **never automatically signed
    in**. Development also remains able to start when bootstrap configuration
    is absent while no Administrator role holder exists (§14.2), strictly for
    inspection/testing: bootstrap performs no writes, no Administrator
    privilege is manufactured, and no credential comes into existence.
8.2 There is no hidden development authentication bypass: no `/dev/login`, no
    fake users, no fake or default credentials, no `admin`/`admin`, no
    hard-coded development passwords, no fake OIDC provider configuration
    (ADR-0015 §14).
8.3 **Decision — Option B:** the first local credential is initialized in an
    environment where SSO works (the Development SSO client of ADR-0006 §3.3,
    or any environment with a working University SSO), using the §6
    operation. Afterwards the legitimate Local Login path (ADR-0015 §1) works
    in Development with or without OIDC configured.
8.4 Rationale: an explicitly invoked Development-only initialization operation
    (Option A) would have to set a password for an account *without any prior
    authentication*, guarded only by an environment check — the exact shape of
    a backdoor and of the removed `/dev/login`, contrary to AGENTS §13.7 and
    ADR-0015 §13/§14. Option B introduces no new mechanism, no new endpoint
    and no fifth project (AGENTS §1), and reuses mechanisms this ADR already
    defines.
8.5 Accepted limitation: a developer machine that can neither reach SSO nor
    present an already-initialized local credential authenticates nobody;
    authenticated development work then requires one SSO-reachable sign-in
    (to initialize) or a previously initialized credential. The test suite is
    unaffected (its OIDC provider is wired by the test host, not by
    application configuration).

### 9. SSO outage behavior

9.1 Before the Administrator has initialized a legitimate local credential:

    ```text
    SSO unavailable + no local credential = Administrator cannot authenticate
    ```

    This is fail-closed and acceptable. **No automatic emergency password is
    ever created.**
9.2 After legitimate credential initialization:

    ```text
    SSO unavailable + valid local Administrator credential = local login may be used
    ```

    per ADR-0015 §1/§10 (explicit user choice, same authorization-ready
    identity, all account-state checks still enforced).
9.3 Break-glass remains a separate, unimplemented, disabled-by-default
    emergency mechanism (ADR-0015 §11). This ADR neither implements nor
    redefines it; §9.1 is not a reason to create one.

### 10. Role infrastructure dependency and deferred decisions

10.1 The required outcome (§3) includes the Administrator role, which depends
     on the UC2 role infrastructure (role store, seeded roles, permission
     mapping). Bootstrap implementation MUST NOT precede that infrastructure;
     if the role infrastructure is unavailable, bootstrap fails closed and
     creates nothing (§14.6).
10.2 ADR-0016 deliberately resolves NONE of the disputed Roles/Permissions
     semantics: `GraduateStaff` vs `Staff`; lowercase-dot vs PascalCase
     permission names; the Student default permission mapping; general
     permission-delegation semantics; general `UserKind` ↔ Role consistency
     for all users. Those belong to the Roles/Permissions administration
     decision, expected as **ADR-0017**.
10.3 For the bootstrap Administrator only, §3 states the required outcome:
     local-auth eligibility and Administrator authorization are established
     through their respective mechanisms; no naming or mapping dispute is
     settled here.

### 11. Interaction with last-Administrator protection

The general last-Administrator invariant belongs to ADR-0017. This ADR
establishes only:

- bootstrap MUST NEVER silently replace an already-established Administrator:
  with an Administrator role holder present it performs no writes at all (§4.2),
  and it never modifies accounts it did not create;
- credential initialization/reset/disable MUST NOT be usable as a shortcut
  around future last-Administrator protections: credential operations change
  credentials only — never roles or UserKind — and each is individually
  audited (§12);
- deleting or changing bootstrap configuration after completion MUST NOT
  remove or alter the established Administrator (§4.3);
- after completion, bootstrap configuration is NOT the source of truth for
  permanent authorization (§4.3).

### 12. Audit

12.1 Minimum conceptual events, named after the `Category.Event` convention of
     `Domain/Auditing/AuditActions.cs` (e.g. `Authentication.LocalSignInSucceeded`).
     The constants are added in the implementation slice, NOT in this ADR:

    | Conceptual event | When | Actor |
    |---|---|---|
    | `Bootstrap.AdministratorProvisioned` | User created with `UserKind = Administrator` | none (startup operation) |
    | `Bootstrap.AdministratorRoleEstablished` | Administrator role assigned to the bootstrap account | none (startup operation) |
    | `Credential.LocalCredentialInitialized` | First local password set (§6) | the authenticated self-initializing account |
    | `Credential.LocalCredentialReset` | Administrative credential reset (§7) | the acting administrator |
    | `Credential.LocalCredentialDisabled` | Local credential disabled (§7) | the acting administrator |

12.2 One record per operation with `Result = Success | Denied | Failed`
     (refusals in §14 are `Denied`, audited with a fixed non-sensitive summary
     and no National ID payload beyond existing masking rules). Bootstrap
     events run without a session, so `ActorUserId` is absent, matching the
     existing precedent in `LocalSignInHandler`; `EntityType` is `User` and
     `EntityId` is the internal user id.
12.3 NEVER audited or logged: password, password hash, password confirmation,
     OIDC token, client secret, security token, or any credential material
     (AGENTS §14, ADR-0015 §16).
12.4 **Persistence sequencing (documented, not glossed):** Phase 1A writes
     audit through the best-effort Serilog sink; the append-only,
     same-transaction `AuditLog` arrives with Phase 1C (ROADMAP). This ADR
     does NOT claim transactional database auditing exists today. Desired
     semantics — bootstrap provisioning and role establishment committed
     atomically with their audit record, credential events likewise, audit
     failure blocking the change — apply from Phase 1C onward; the
     implementation slice must re-verify audit behavior when Phase 1C ships.
     Until then an audit-write failure is logged as an error and the operation
     still completes (current best-effort architecture, AGENTS §14 ordinary
     events), and this gap must remain visible in the implementation report.

### 13. Explicitly rejected (security)

The following are rejected and must never exist as part of bootstrap or the
credential lifecycle:

- default passwords; seeded passwords; shared administrator credentials;
  hard-coded credentials;
- passwords or password hashes in `appsettings*.json` or any checked-in,
  environment-specific file;
- manual password-hash SQL manipulation as a supported workflow;
- `/dev/login` or any equivalent endpoint;
- automatic login after bootstrap;
- authentication based only on the configured National ID;
- authorization based on `UserKind`.

Manual direct-database manipulation is NOT a supported bootstrap or
credential-lifecycle workflow. The only supported paths are: bootstrap (§1–§4),
the credential-initialization operation (§6), and authorized administrative
functionality (§7).

### 14. Failure behavior (fail-closed)

14.1 **Malformed bootstrap National ID** (or mobile): present-but-invalid
     configuration fails startup validation (§2.4). Nothing is provisioned
     from invalid input; no partial account is ever created.
14.2 **Missing bootstrap configuration while no Administrator role holder
     exists** — split by environment:
     - **Non-Development (Production, Staging, Testing): startup MUST fail
       closed** with a clear configuration/operational error and **no
       user/account mutation**. The application does not start half-configured
       and never runs a degraded "warning-only" bootstrap, so a fresh
       deployment cannot sit running with no supported Administrator creation
       path (consistent with AGENTS §13.8 fail-closed and `ValidateOnStart`,
       AGENTS §18).
     - **Development:** startup may proceed for developer inspection/testing,
       but bootstrap performs **no writes**: no user is created, no
       Administrator privilege is manufactured, no fake or default credential
       exists, no user is automatically authenticated, and `/dev/login` stays
       absent (§8). Students are unaffected (SSO provisioning is independent).
     In both branches there is no elevation and no partial provisioning; once
     an Administrator role holder exists, absent configuration is legitimate
     (§2.4) because bootstrap has completed and configuration is no longer
     authoritative.
14.3 **Duplicate/conflicting National ID:** duplicates are impossible at the
     database level (unique index, ADR-0001 §6). Any conflicting identity
     state → refuse, audit `Denied`, no writes.
14.4 **Bootstrap target already exists as a Student** → refuse automatic
     elevation; no fields are changed; audit `Denied`.
14.5 **Bootstrap target already exists as Staff** → likewise refuse automatic
     elevation (ambiguous state; §4.5). No automatic Student→Administrator or
     Staff→Administrator conversion ever happens at startup.
14.6 **Administrator role infrastructure unavailable** (UC2 not present, role
     store or role seed missing) → bootstrap refuses entirely and creates
     nothing; no half-provisioned privileged account.
14.7 **Identity operation failure:** if user creation succeeds but role
     assignment fails, the outcome is an inert kind-only account (eligible for
     local login, zero authorization); the event is audited `Failed`, the next
     startup resumes by assigning the role to the configured identity (§4.5),
     and any state not matching §4.5 is ambiguous → refuse (§14.9).
14.8 **Audit failure:** under the currently available Phase 1A architecture
     (best-effort Serilog sink), a failure is logged as an error and the
     operation completes (§12.4); from Phase 1C the desired semantics are
     transactional and blocking. The failure mode is never silent in either
     regime.
14.9 **Ambiguous existing user state** (any state not explicitly allowed by
     §4.5, including an `UserKind = Administrator` account that is not the
     configured identity): prefer refusing automatic elevation and requiring
     an explicit administrative/recovery decision. No destructive
     reconciliation, no guessing.
14.10 **SSO unavailable before credential initialization:** the Administrator
     cannot authenticate and cannot reach §6; fail closed (§9.1). No emergency
     credential is created.

## Alternatives considered

### Bootstrap mechanism

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Startup/configuration bootstrap (chosen) | Matches ADR-0002 §2.2 verbatim; no secret anywhere; idempotent; no extra project; no SSO dependency for provisioning | Needs strict completion/refusal rules to avoid becoming a privilege-sync channel | Chosen — §§2–4 |
| SSO-first bootstrap (promote on first SSO login matching configured identity) | Identity proofed by IdP before any state change | Promotion logic hidden inside a sign-in callback; harder to audit/idempotency-test; configuration still required, so no simplification | Provisioning stays separate from authentication (§5) |
| One-time administrative CLI/tool | Explicit invocation | Extra project/tooling → ADR required (AGENTS §1); deployment burden (ADR-0006 manual checklist); still cannot solve the first *credential* | Conflicts with four-project lock; no added value over §1 |
| Manual database bootstrap | None needed to build | Unaudited, unsupported, bypasses validation, invites hand-written password hashes | Explicitly rejected (§13) |

### First credential / Development mechanism

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| SSO-authenticated self-initialization (chosen) | Uses an authenticated, audited, owner-only path; no token subsystem; no secret in config | Requires SSO reachable once before local fallback exists (accepted, §9.1) | Chosen — §6 |
| Administrator-chosen/generated temporary password | Works without SSO | Administrator sees or chooses credential material; shared/known secret exists temporarily | Rejected by §13 and ADR-0015 §8 |
| One-time reset token (link/SMS/email) | Familiar UX | New token/UX subsystem undecided; depends on unscheduled OTP/notification infrastructure; over-engineering for one account | Deferred with §7 rather than invented here |
| Development-only unauthenticated initialization operation (Option A) | Works fully offline | Password-setting without prior authentication guarded only by an environment check — a backdoor shape; violates AGENTS §13.7 and ADR-0015 §13/§14 | Rejected — §8.4 |
| Require SSO-based first initialization, then legitimate local login in Development (Option B) | No new mechanism, no bypass, four projects untouched | Needs SSO reachability once | Chosen — §8.3 |

## Consequences

- Bootstrap implementation is now an explicitly decided Phase 1A slice, but it
  is ordered **after UC2** (role infrastructure, §10.1) and its credential step
  is ordered after the first SSO sign-in (§6).
- Positive: a first-Administrator path exists with zero secrets in
  configuration or source, no development backdoor, and no second user type;
  ADR-0002 §2.2 and ADR-0015 §7 now have concrete operational semantics.
- Negative/limits: until §7's deferred UX/token decision, Staff accounts have
  no local credential and remain SSO-dependent; a Development machine without
  SSO reachability and without an initialized credential cannot authenticate
  (§8.5, accepted).
- The completion/refusal rules (§4, §14) are deliberately conservative:
  ambiguous states stop and ask a human instead of reconciling automatically.
- Documents changed by this decision: `docs/decisions/README.md` (index),
  `AGENTS.md` §24 and `ROADMAP.md` (ADR schedule/gate for Phase 1A). ADR-0002
  rule 2.2 is operationalized, not edited; ADR-0015 is complemented, not
  superseded; ADR-0017 is expected to carry the deferred Roles/Permissions
  administration decisions.
- Follow-up work: UC2 (roles/permissions, `PermissionPolicyProvider`,
  `ICurrentUser`) → bootstrap slice → credential-initialization operation →
  UC3 (user administration) → ADR-0017.

## Security impact

- New attack surface: one config-gated startup routine that writes Identity
  rows without authentication, and one authenticated owner-only
  credential-setting operation. Both are bounded by the guards in §4, §6 and
  §14; neither can create a session.
- Configuration change after completion cannot move privilege (§4.3) — the
  main abuse vector of config-driven provisioning is closed by making
  configuration consumable only before an Administrator role holder exists.
- No new secrets: configuration holds identity data only (§2.2); passwords
  never exist outside an Identity password-hash write inside the request.
- Authentication/authorization unchanged: ADR-0015 returnUrl, enumeration
  resistance, lockout, rate limiting, per-request security-stamp validation
  and the UserKind/authorization boundary all remain as-is; this ADR adds no
  endpoint to the anonymous allow-list.
- Audit: the five conceptual events (§12) are defined with credential-free
  payloads; the Phase 1A best-effort vs Phase 1C transactional gap is
  documented rather than hidden (§12.4).

## Migration / rollout impact

- **No schema change from this ADR.** Bootstrap reuses the existing
  `AspNetUsers` table; the Administrator role requires the UC2 role-store
  migration, which is a separate, already-anticipated slice. No migration is
  created by this ADR-writing task.
- Configuration rollout: add the two empty `BootstrapAdministrator:*` keys;
  supply values per environment (environment variables / user-secrets), never
  committed. Removing the values after completion is safe (§4.3).
- Deployment order: ADR-0016 (this document) → UC2 role infrastructure →
  bootstrap + credential-initialization slice → UC3 administration → ADR-0017
  decisions as they gate their slices. Production migrations continue to be
  applied by the reviewed script of ADR-0006 §9.3; bootstrap runs at startup
  afterward and needs no script of its own.
- Rollback: disabling bootstrap configuration stops all future provisioning
  writes; it never deletes established accounts. No data backfill is implied.

## Acceptance criteria

1. No default, seeded, shared or generated password exists anywhere in the
   system as a result of bootstrap or the credential lifecycle.
2. No password or password hash is stored in source control, `appsettings*.json`,
   checked-in environment-specific files, or any configuration.
3. Bootstrap is idempotent: repeated startups create no duplicate users, no
   duplicate role assignments, no credential resets, no unnecessary
   SecurityStamp rotations, no overwritten profile fields.
4. After completion, changing or deleting the bootstrap configuration never
   transfers, removes or alters Administrator privilege; startup performs no
   writes while any Administrator role holder exists (§4.2, bootstrap guard
   only — not the general last-Administrator invariant, see §11).
5. Bootstrap uses the normal `ApplicationUser`; no `BootstrapUser` or second
   user record exists.
6. `UserKind = Administrator` grants no authorization; no code path uses
   UserKind as an authorization check.
7. Administrator authorization comes from the Administrator role through the
   normal role/permission system (UC2), not from bootstrap state.
8. Bootstrap provisions only: it never creates a session, never signs anyone
   in, and never bypasses or skips OIDC validation.
9. `/dev/login` remains absent; no development authentication bypass exists.
10. Student local login remains forbidden (ADR-0015 §6), unchanged by this ADR.
11. The first local credential is obtainable only through the §6
    SSO-authenticated self-initialization operation; no alternative
    credential-creation path exists.
12. SSO outage before credential initialization fails closed: the
    Administrator cannot authenticate, and no emergency credential is created.
13. After legitimate local credential initialization, an SSO outage uses
    ADR-0015 local fallback with identical authorization and account-state
    enforcement.
14. Authentication method does not alter Roles, Permissions, StaffScope,
    ownership or account-state enforcement (ADR-0015 §5 unchanged).
15. Bootstrap and credential events are auditable without credential material
    (§12); no password, token or secret appears in any audit or log record.
16. Break-glass remains separate, unimplemented and untouched by this ADR.
17. Existing ADR-0015 returnUrl/open-redirect and login security behavior
    remains unchanged; no anonymous endpoint is added.
18. This ADR-writing task created no database migration and no production
    code.
19. Outside Development, startup fails closed with a clear configuration
    error and no user/account mutation when no Administrator role holder
    exists and `BootstrapAdministrator` configuration is absent; once an
    Administrator role holder exists, absent configuration starts normally.
    Development may start in that state but creates no user, no credential
    and no session.
