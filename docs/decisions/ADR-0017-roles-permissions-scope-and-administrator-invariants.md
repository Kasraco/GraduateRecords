# ADR-0017: Roles, permissions, scope, and Administrator invariants

- **Status:** Accepted
- **Date:** 2026-10-30
- **Deciders:** Project Owner
- **Related documents/ADRs:** AGENTS.md §2 (project boundaries), §13 (authentication and authorization), §14 (audit), §17 (seeds/migrations), §24 (ADR schedule) · ARCHITECTURE.md §10 (authorization), §11 (audit), §15 (security boundaries) · OPENCODE-MASTER-INSTRUCTIONS.md §8 (authorization) · ROADMAP.md Phase 1A · ADR-0002 (provisioning/activation — clarified), ADR-0004 (staff data scope — reaffirmed and partially superseded as listed in §23), ADR-0015 (dual authentication — reaffirmed), ADR-0016 (bootstrap Administrator — its explicit deferrals are resolved here)

## Context

ADR-0016 deliberately deferred the general Roles/Permissions administration
decisions to this ADR (ADR-0016 §10.2 and §11), and the pre-implementation
inspections of Phase 1A found several contradictions between binding documents
that must be settled before UC2 (the roles/permissions slice) can be
implemented without guessing:

1. **Role naming.** ADR-0004 §1.1 (Accepted) defines the roles as
   `Student`, `GraduateStaff`, `Administrator`, while ROADMAP Phase 1A's
   features line says "seeded Student/Staff/Administrator roles". One exact
   string is required for seeded Identity role names.
2. **Permission naming.** ADR-0004's table uses PascalCase.Dot
   (`Students.View`, `Users.Manage`); ARCHITECTURE §10 lists illustrative
   lowercase.dot examples (`students.view`, `users.manage`), echoed in AGENTS,
   DESIGN and ROADMAP prose. Both forms appear in binding documents; permission
   strings are compiled into policies and seeded into claims, so one canonical
   runtime convention is required.
3. **Delegation contradiction.** ADR-0004 §2 states that "an actor cannot
   grant a permission (or a role carrying it) that the actor does not hold".
   ADR-0004's own mapping table gives the Administrator only `Users.Manage`,
   `Settings.Manage`, `Audit.View` and a limited `Reports.Export` — none of the
   GraduateStaff permissions. Combined with ADR-0004 §2's statement that
   "Assigning roles to users is in scope (ROADMAP Phase 1A)", the rule makes
   the in-scope duty impossible: an Administrator could never assign the
   GraduateStaff (or Student) role. (ADR-0004 attributed the rule to
   "AGENTS §13"; AGENTS §13 contains no delegation rule, so ADR-0017 now
   provides the single authoritative delegation statement.)
4. **Forms/Workflows ownership.** ADR-0004's table assigns `Forms.Manage` and
   `Workflows.Manage` to GraduateStaff, while ARCHITECTURE §1 (Administrator
   actor: "Design/publish forms, assign forms, define workflow revisions") and
   ROADMAP Phase 2 ("admins can design, version, publish and assign forms")
   make the Administrator the intended owner of definition management.
5. **Seeding strategy.** ADR-0004 §2 reads as seeding the whole default
   role-to-permission mapping at once, while OPENCODE §8 prescribes the
   incremental convention ("New permissions: add the constant, seed it,
   document it in ARCHITECTURE §10") and ROADMAP gates every feature by phase.
   Seeding later-phase permissions in Phase 1A would create capabilities with
   no enforcing feature.
6. **Student mapping.** ADR-0004 §2 requires student endpoints to carry
   `[HasPermission(...)]`, yet its mapping table gives the Student role zero
   permissions; the Student role's permission set is undefined.
7. **Last Administrator.** ADR-0002 §7.3 ("cannot deactivate the last active
   Administrator") and ADR-0004 §1.3 (self-role-change ban) exist, but no
   general invariant defines what counts as an Administrator, what "active"
   means, whether role removal is covered, or what happens when two
   Administrators act concurrently. ADR-0016 §11 deferred all of this here.
8. **UserKind ↔ Role.** ADR-0015 §2's matrix speaks of "user types" without
   binding that term to UserKind or Role; ADR-0002's data model lists
   `UserKind` as (Student / Staff) while the implementation carries three
   values; and the UserKind of a person who is both student and staff member
   is undefined.
9. **StaffScope.** ADR-0004 §4.1 fixes Phase 1 scope to all students, but the
   StaffScope value for Student and Administrator accounts, and whether scope
   is persisted or derived, are not stated.

UC2 cannot be implemented until these are settled. This ADR also absorbs the
long-promised "future ADR on Roles/Permissions" referenced by ADR-0001,
ADR-0002, ADR-0004 and ADR-0015.

## Decision

### 1. Authorization model: four separated concepts

**Problem.** Authentication, account categorization, authorization and resource
access have been discussed in overlapping terms, which produced the UserKind
and delegation ambiguities above.

**Rule.** The model is:

```text
AuthenticationMethod
    → how the user authenticated (University SSO or local credential)
    → NEVER grants authorization

UserKind
    → authentication eligibility / account category
    → NEVER grants authorization

Role
    → groups authorization capabilities

Permission
    → authorizes an operation

Ownership
    → restricts which owned resource may be accessed

StaffScope
    → restricts which student resources GraduateStaff may access
```

Permissions are carried by roles; effective access to a resource normally
requires a **combination**, depending on the resource and actor:

```text
Permission + Ownership      (student-owned resources)
Permission + StaffScope     (GraduateStaff access to student resources)
Permission alone            (administrative-plane endpoints)
```

No authorization check may read `AuthenticationMethod` or `UserKind`.
This extends ADR-0015 §5 (authentication *path* independence) with the
account-category dimension and makes the previously implicit combination
model of ARCHITECTURE §10 ("Resource ownership + Staff scope (inside
handlers)") an explicit rule of this ADR.

**Why.** Each concept answers a different question; conflating them is what
made the ADR-0004 delegation rule and the UserKind questions unresolvable.
Fail-closed behavior (§21) depends on a clean separation: a missing role or
permission can never be papered over by an authentication attribute.

### 2. Canonical roles and naming

**Problem.** `GraduateStaff` (ADR-0004 §1.1, Accepted) and `Staff`
(ROADMAP Phase 1A prose) name the same middle role differently.

**Rule.** The canonical role names — the exact persisted/seeded Identity role
names — are:

```text
Student
GraduateStaff
Administrator
```

`Staff` may continue to be used as a generic prose term for employees, but
MUST NOT be used as the canonical Identity role name.

**Why.** ADR-0004 is the Accepted decision that defined the role model; an
Accepted ADR outranks roadmap prose (README: ADRs are the only approved way
to deviate from the documents). One exact string is required for seeds,
claims and tests. ROADMAP's Phase 1A seed instruction is corrected at
registration time to `Student/GraduateStaff/Administrator`; no broad
terminology rewrite is performed.

### 3. Multi-role membership

**Problem.** A person may be both a student and a staff member; single-role
assumptions would break identity and conflict-of-interest rules.

**Rule.** A user MAY hold multiple roles, e.g. `Student + GraduateStaff` or
`Student + Administrator`. Role membership is independent of authentication
method. The conflict-of-interest rule is preserved unchanged: a user MUST NOT
decide on a submission or resource decision belonging to themselves merely
because they also hold GraduateStaff or another privileged role
(ADR-0004 §4.3; AGENTS §10 "actor ≠ owner"). Ownership checks in the
owning feature handlers enforce this; role membership never overrides it.

**Why.** Reaffirms ADR-0004 §1.2 and the substance of ADR-0001 rule 10
(one user per National ID with multiple roles) without any single-role
assumption entering UC2.

### 4. UserKind and Role are separate

**Problem.** UserKind is single-valued while roles are multiple; the docs
never said how the two relate, and ADR-0016 correctly deferred this.

**Rule.** UserKind is a single account/authentication category. The canonical
categories are the three currently supported values:

```text
Student
Staff
Administrator
```

(ADR-0002's two-value data-model note `(Student / Staff)` is clarified to
these three; the Emergency/Break-glass identity of ADR-0015 §11 is a separate
mechanism, not a UserKind value.) UserKind controls local-authentication
eligibility per ADR-0015 §2 and ADR-0016; Roles control authorization.

For a person who is both a student and a staff member, the intended model is:

```text
UserKind = Staff

Roles:
- Student
- GraduateStaff
```

This preserves Staff local-authentication eligibility while authorization
remains role/permission based. For a bootstrap Administrator established
under ADR-0016, the intended normal state is:

```text
UserKind = Administrator
Role = Administrator
```

However, **no authorization policy may infer Administrator authority from
UserKind alone.** If UserKind and role membership become inconsistent,
authorization fails closed with respect to privileges not represented by
actual roles/permissions (§21). No role is auto-granted because UserKind has
a corresponding value, except through the explicitly defined ADR-0016
bootstrap/provisioning workflow.

**Why.** This answers the dual-role-person question without weakening
ADR-0015 §6 (Students are SSO-only): a Student+GraduateStaff person with
`UserKind = Staff` remains local-login eligible, and their student
authorization still comes only from the Student role. The fail-closed clause
prevents UserKind from ever becoming a backdoor to privileges.

### 5. Canonical permission naming

**Problem.** Two documented conventions (PascalCase.Dot vs lowercase.dot).

**Rule.** Canonical permission identifiers use **PascalCase.Dot**:

```text
Users.Manage
Roles.Assign
Students.View
Forms.Manage
Submissions.View
```

Existing lowercase examples in other documents (ARCHITECTURE §10, AGENTS §9,
DESIGN, ROADMAP prose) are illustrative and do NOT establish a second runtime
naming convention; ADR-0017 is authoritative for naming semantics. All new
permission constants, seeded claim values and policy names use the canonical
form. The illustrative lowercase examples are not mechanically rewritten in
this task; owning slices update the documentation they touch, per
OPENCODE §8 ("document it in ARCHITECTURE §10" when a permission is added).

**Why.** ADR-0004 (Accepted) already uses PascalCase.Dot in the only
role→permission mapping that exists; PascalCase matches the C# constants that
AGENTS §2 assigns to the Application layer. Choosing the Accepted form
requires superseding nothing; choosing lowercase would silently invalidate an
Accepted ADR's own table.

### 6. Permission model (v1): permissions belong to roles only

```text
User
  → one or more Roles
      → zero or more Permissions
```

Permissions are assigned to roles. **Direct per-user permission assignment
is NOT supported in v1.** There is:

- no permission inheritance;
- no role hierarchy (`Administrator > GraduateStaff` is NOT an authorization
  rule);
- no wildcard permission semantics (`Forms.*`, `Students.*`, `Submissions.*`
  are documentation shorthand in prose, never runtime permissions);
- no implicit "Administrator has everything" rule.

Every effective permission must be explicit. Administrator's authority comes
only from the permissions explicitly mapped to the Administrator role (§9,
§13) plus the administrative capabilities it holds.

**Why.** ARCHITECTURE §10's chain (`user → Role → Permission → Policy`) has
always implied roles-only assignment; making it explicit closes the
"direct user permissions?" ambiguity and prevents wildcard/hierarchy
assumptions from leaking into UC2 implementation.

### 7. Catalog, phase-specific seed set, and runtime enforcement

ADR-0017 distinguishes three levels, which earlier documents partially
conflated:

1. **Permission architecture/catalog** — the architectural vocabulary
   (ARCHITECTURE §10's "example permissions", ADR-0004's table, this ADR's
   names). It may describe permissions belonging to later features.
2. **Phase-specific seed set** — only permissions required by
   implemented/owning feature slices are added to runtime constants, role
   mappings and idempotent seeds.
3. **Runtime authorization** — policies/handlers/endpoints enforce
   permissions when the corresponding feature is implemented.

**Incremental strategy (adopted):**

> A feature-owning phase introduces/finalizes its permission constant, role
> mapping, idempotent seed extension, authorization policy/handler, tests and
> required documentation when that feature is implemented.

This **supersedes any interpretation of ADR-0004 §2** that requires the entire
long-term role-to-permission table to be seeded wholesale during Phase 1A.
UC2 does not seed all permissions mentioned by ADR-0004 or the illustrative
ARCHITECTURE §10 examples.

**Why.** OPENCODE §8's "new permissions" convention, AGENTS §17's idempotent
seeds and ROADMAP's per-phase gating all describe incremental introduction;
seeding later-phase permissions in Phase 1A would create capabilities with no
enforcing endpoint and would freeze granularity decisions before their
features exist.

### 8. Phase 1A (UC2) seed set

The only permission capabilities Phase 1A currently requires are:

```text
Users.Manage
Roles.Assign
```

The roles seeded by UC2 are `Student`, `GraduateStaff`, `Administrator`.
The Phase 1A default mapping is:

```text
Student
    (no Phase-1A feature permissions)

GraduateStaff
    (no Phase-1A feature permissions)

Administrator
    Users.Manage
    Roles.Assign
```

An empty Phase-1A permission set for Student and GraduateStaff is
**intentional**. It does NOT mean those roles will never receive
permissions; their permissions are extended idempotently by the owning later
feature phases (§7, §14).

The following are explicitly **not** added to the Phase 1A seed merely for
completeness: `Settings.Manage`, `Audit.View`, `Forms.*`, `Students.*`,
`Submissions.*`, `Workflows.*`, `Reports.*`, `Documents.*`.

**Why.** These two capabilities are the only ones with a demonstrated Phase
1A requirement: UC3's admin UI for users/roles (ROADMAP Phase 1A features)
cannot gate its endpoints without them. Everything else would be speculative.

### 9. Users.Manage

`Users.Manage` is the Phase 1A administrative capability for the supported
**user-account lifecycle** operations that UC3 actually implements (e.g.
creating users, enabling/disabling accounts, account-state changes).

It is **not** a catch-all for role assignment: role grant/revoke is the
separate `Roles.Assign` capability (§10). Future account operations must
still obey independent invariants such as last-Administrator protection
(§17-§19).

**Why.** Splitting the capabilities keeps the escalation surface of each
capability reviewable and matches ADR-0015 §7's requirement that lifecycle
operations be individually controlled and audited.

### 10. Roles.Assign

`Roles.Assign` means:

> Allows an authorized Administrator to grant and revoke supported roles for
> users, subject to self-role-change restrictions (§20), supported-role
> rules, conflict-of-interest rules where applicable (§3), and the
> last-Administrator invariant (§17).

It covers **both** directions:

```text
Grant role
Revoke role
```

Separate `Roles.Grant` / `Roles.Revoke` permissions are NOT introduced in v1.
`Roles.Manage` is NOT the canonical Phase 1A permission, because v1 provides
no runtime creation/editing of arbitrary role definitions or arbitrary
role→permission mappings: role definitions and mappings are code-defined and
seeded (§7, §8), and an admin UI for editing arbitrary role→permission
mappings remains out of scope for v1 (preserving ADR-0004 §2) unless a future
Accepted ADR changes that.

**Why.** The name must describe what v1 actually enforces. `Roles.Manage`
would imply definition-management powers that do not exist; grant/revoke is
one operational capability in every admin UI design considered.

### 11. Delegation and escalation protection (partially supersedes ADR-0004 §2)

**Superseded rule.** ADR-0004 §2's rule that, in substance, "an actor cannot
grant a permission, or a role carrying it, that the actor does not hold" is
**partially superseded** by this ADR. It is incompatible with the separated
Administrator plane: under ADR-0004's own mapping, the Administrator holds
none of the GraduateStaff permissions, so the rule made the in-scope role
assignment duty impossible (Context item 3).

**New rule.** An Administrator holding `Roles.Assign` MAY assign a supported
role such as `GraduateStaff` even when the Administrator does not personally
hold each permission carried by GraduateStaff. Example:

```text
Administrator:
    Users.Manage, Roles.Assign

GraduateStaff (typical later-phase mapping, illustrative):
    Students.View, Submissions.View, Submissions.Decide
```

The Administrator does NOT need `Students.View`, `Submissions.View` or
`Submissions.Decide` merely to assign the GraduateStaff role. Role assignment
does not grant those permissions to the Administrator.

**Escalation protection, redefined.** The project's permission-escalation
protection concept is preserved, restated consistently with the new model:

- only supported, code-defined roles may be assigned;
- direct arbitrary permission grants are not supported at all (§6);
- arbitrary role creation/editing is not supported in v1 (§10);
- the actor must hold the administrative capability (`Roles.Assign`) required
  to perform role assignment;
- protected invariants — last-Administrator (§17-§19) and self-role-change
  (§20) — remain enforced on every role operation;
- no authentication path and no UserKind value may bypass these rules (§1, §4).

ROADMAP Phase 1A's "permission-escalation check" and its escalation tests are
therefore satisfied by this redefined protection set, not by the superseded
ADR-0004 sentence.

**Why.** Role assignment is a *capability about roles*, not an exercise of
the assigned role's permissions. The escalation risk the old rule addressed
is real but is fully covered by the redefined set: an attacker who gains
`Roles.Assign` can assign any supported role — yet cannot invent roles,
permissions, or permissions-on-users, and cannot bypass the invariants. No
"Super Administrator" concept is introduced.

### 12. Administrator data boundary

Administrator is a system-administration role. Administrator **MUST NOT**
implicitly receive student-data access. In particular, Administrator does
not gain `Students.View`, `Submissions.View` or `Submissions.Decide` merely
because it is Administrator; Administrator authority is explicit-permission
based (§6). The architectural separation of the Administrator plane from the
GraduateStaff student-data plane is preserved (ADR-0004 §8, §7.3, its
acceptance criteria, and ARCHITECTURE §15's "Admin plane: separate area,
explicit permissions, audited, last-admin protection").

This ADR does not grant Administrator any of those permissions in any phase;
the later-phase mapping decisions belong to the owning phases (§13, §14).

**Why.** Least privilege and separation of duties are the reason the
Administrator's ADR-0004 permission set is small; the delegation change
(§11) must not be read as an opening to hand the admin plane student data.

### 13. Form and workflow definition ownership

**Problem.** ADR-0004's table assigned `Forms.Manage`/`Workflows.Manage` to
GraduateStaff, while ARCHITECTURE §1 and ROADMAP Phase 2 make the
Administrator the intended owner of form/workflow definition management.

**Rule.** The Administrator is the intended administrative owner of
form-definition and workflow-definition management. When the owning feature
phases (Phase 2 for forms, Phase 4 for workflows) are implemented, the
canonical equivalents of:

```text
Forms.Manage
Workflows.Manage
```

belong to **Administrator**, rather than being used as a way to grant
student-data access. The ADR-0004 mapping rows that assigned these to
GraduateStaff are superseded. Explicitly:

```text
Forms.Manage   does not imply Submissions.View.
Workflows.Manage does not imply Submissions.View.
```

Managing definitions/process configuration does not itself authorize viewing
student submissions. These permissions are **not** seeded in Phase 1A; their
exact final inventory/granularity is finalized by the owning feature phase
under this ADR's rules (§5, §7). Changes to published/effective workflow
definitions must be auditable per the project's audit architecture when that
feature exists (ROADMAP Phase 4 "audit of every decision"; AGENTS §14).

**Why.** Definition management is system configuration, not student-data
access; the previous mapping contradicted two other binding documents and
would block the documented Phase 2 goal. "Finalized by the owning phase"
leaves e.g. any GraduateStaff preview/read needs to be settled where the
feature exists, not guessed now.

### 14. Future feature permissions

This ADR establishes the naming and model rules (§5, §6) that future
permissions must follow; it does **not** freeze the illustrative
ARCHITECTURE §10 examples as final runtime constants. Examples such as
`Forms.Publish`, `Forms.Assign`, `Submissions.Create`, `Submissions.ViewOwn`,
`Students.Sync`, `Workflows.Publish` and `Documents.DownloadAny` are
finalized (existence, exact granularity, mapping, seed, policy, tests) by
their owning phases.

In particular, this ADR does NOT resolve prematurely whether
workflow/review authorization ultimately uses a single `Submissions.Decide`
capability (ADR-0004's table) or more granular capabilities such as
`Submissions.Review`, `Submissions.Approve`, `Submissions.Reject`,
`Submissions.Return` (ARCHITECTURE §10's illustrative split). That decision
belongs to the owning workflow/review phase (Phase 4), which must apply §5
naming, §6 model and §7 incremental seeding.

### 15. Students.EditOwnProfile and Students.EditProfile

`Students.EditOwnProfile` is **not introduced**. The repository contains no
authoritative definition for it, and ADR-0004 §3.2 states that students
cannot edit their profile in the first release. Inventing the permission
would contradict an Accepted ADR.

`Students.EditProfile` exists conceptually (ADR-0004 §6) but currently has no
clearly scheduled owning ROADMAP phase. It is **deferred**: not seeded in
Phase 1A, and its semantics are revalidated when its owning feature/phase is
explicitly scheduled. This task does not invent a ROADMAP feature to
accommodate it.

### 16. Resource authorization: ownership and scope compose with permission

Role/permission checks alone are not sufficient for resource access where
ownership or staff scope applies. The architecture's model is preserved:

```text
Student resource access:           Permission + Ownership
GraduateStaff student-resource access: Permission + StaffScope
```

where the owning feature requires those checks (ARCHITECTURE §10; ROADMAP
cross-phase invariant "permission + ownership + staff scope in every
handler"). Out-of-ownership and out-of-scope behavior remains consistent with
the existing architecture, including **NotFound** where already required to
prevent resource enumeration (student-other ⇒ NotFound; out-of-scope ⇒
NotFound). Administrator does not bypass ownership/scope restrictions simply
by being Administrator.

**Why.** This keeps ADR-0004 §2/§10 and AGENTS §23.3 intact and prevents the
delegation change (§11) from being misread as an admin bypass of resource
boundaries.

### 17. Last-Administrator invariant (operation-agnostic)

The last-Administrator rule is an invariant over **system state**. It MUST
NOT be defined as a rule attached only to `Roles.Assign` or `Users.Manage`:

> Any operation, through any code path or permission, that could leave the
> system without a valid/effective Administrator must enforce the
> last-Administrator invariant.

Current examples include, but are not limited to:

- revoking the Administrator role;
- deactivating/disabling an Administrator account;
- deleting an Administrator, if deletion is ever supported;
- changing account state in a way that makes the Administrator unusable;
- changing UserKind/authentication eligibility in a way that would make the
  last Administrator unable to use the supported administrative
  authentication path;
- credential lifecycle operations, if they could effectively make the last
  Administrator inaccessible.

The invariant is operation-agnostic: any future feature that mutates
authorization-relevant state inherits it.

**Why.** ADR-0002 §7.3 covered deactivation and ADR-0015 §7 warned that the
local-auth workflow must not bypass "last-active-administrator protections",
but neither defined the general rule; a capability-attached rule would be
routed around by the next mutating feature.

### 18. What counts as an effective Administrator

For **authorization and last-Administrator counting**, the authoritative
privilege indicator is:

```text
Role = Administrator
```

NOT `UserKind = Administrator`, and NOT merely possession of an arbitrary
permission such as `Users.Manage`.

Because the invariant is about preserving a usable administrative account,
not a stale role row, an **effective Administrator** for this invariant is an
account that:

1. holds the canonical `Administrator` role;
2. is not administratively disabled/deactivated under the account-state model
   that applies at that time;
3. is not in a permanently unusable state that the application itself
   controls.

Temporary authentication conditions — notably an ordinary temporary lockout —
MUST NOT by themselves cause destructive role/account operations to
reinterpret the user as "not an Administrator" and thereby permit removal of
the remaining Administrator. (Account-state fields such as `IsActive` are not
implemented yet; until ADR-0002's access-state model exists, condition 2 has
no states to evaluate and counting is by role membership plus condition 3.
This sequencing is deliberate, not an omission.)

If authentication-eligibility consistency is required for an Administrator
account (the ADR-0016 §3 normal state `UserKind = Administrator`), it is
enforced as a separate **account-consistency** invariant — never as the
source of Administrator authorization.

**Why.** Role membership is the only authorization-authoritative fact (§1,
§6); counting by UserKind would resurrect UserKind-as-authorization, and
counting by "any enabled-looking account" would let a lockout window delete
the last admin.

### 19. Last-Administrator concurrency

Two concurrent operations MUST NOT both succeed if their combined result
would leave zero effective Administrators. At least one operation must
fail/retry/conflict such that the invariant remains true after the dust
settles.

This is a required semantic outcome, not an implementation mandate: no SQL
isolation level, locking primitive or persistence mechanism is prescribed
here; the implementing use case chooses the strategy (within the project's
existing `RowVersion`/idempotency framework, AGENTS §11).

**Why.** Without the stated outcome, two Administrators could each pass a
"not last" check and jointly remove both accounts. The outcome is decidable
now; the mechanism is an implementation concern of the enforcing slice.

### 20. Administrator self-role change

An Administrator **cannot change their own roles** through the normal
role-administration workflow (preserving ADR-0004 §1.3). This is separate
from last-Administrator protection and remains prohibited even when multiple
Administrators exist, unless a future Accepted ADR explicitly changes the
rule.

**Why.** Self-role-change is a distinct escalation vector (an admin silently
widening or shedding their own capabilities, the latter interacting with
counting in §18); the Accepted rule is kept unchanged.

### 21. Fail-closed authorization

Authorization fails closed. At minimum:

- missing required permission → **Forbidden**;
- unknown/unregistered permission → **no authorization** (never treated as
  granted);
- missing role membership → no role-derived authorization;
- UserKind must not substitute for a missing role;
- AuthenticationMethod must not substitute for a missing role/permission;
- UI visibility is UX only, never security (DESIGN §3; ADR-0004 §10.1);
- server-side authorization is authoritative (ADR-0004 §10.1-§10.2: an
  endpoint without an explicit policy is a build/review failure).

Existing **NotFound** behavior for ownership/scope failures is preserved
where the architecture already requires it (§16).

**Why.** These rules are the enforcement face of §1 and §4: every "must not
substitute" line blocks a specific shortcut that the separated model would
otherwise leave to chance.

### 22. Audit of authorization events

Conceptual auditable events (constants added in the implementing slice,
following the existing `Category.Event` convention of
`Domain/Auditing/AuditActions.cs`, e.g. `Authentication.LocalSignInSucceeded`
— the same precedent ADR-0016 §12.1 followed):

```text
Authorization.RoleGranted
Authorization.RoleRevoked
Authorization.UserEnabled
Authorization.UserDisabled
```

and, when the future functionality exists:

```text
Authorization.RolePermissionMappingChanged
Authorization.StaffScopeChanged
```

These cover ADR-0002 §9's "role granted/revoked" and ARCHITECTURE §11's
"role/permission/user changes" categories; no duplicate event names are
introduced where an existing constant already covers the action.

**Persistence sequencing (unchanged from ADR-0016 §12.4, restated so this ADR
cannot be misread):** in Phase 1A, audit goes through the current best-effort
Serilog sink; the transactional append-only `AuditLog` arrives in Phase 1C.
This ADR does NOT claim transactional audit persistence exists in Phase 1A;
desired same-transaction semantics for these events apply from Phase 1C.

Sensitive data, credentials, tokens, password material, hashes and
authentication secrets are never written to audit events (AGENTS §14,
ADR-0015 §16).

### 23. Relationship to previous ADRs

| ADR | Reaffirms | Clarifies | Partially supersedes |
|---|---|---|---|
| **ADR-0002** | No self-registration; controlled provisioning; last-active-Administrator protection as a requirement (§7.3) | §7.3's deactivation rule is an *instance* of the operation-agnostic invariant (§17) counted by §18; the data-model note `UserKind (Student / Staff)` is clarified to the three canonical categories (§4); provisioning creates accounts, authorization comes only from roles (§1) | — |
| **ADR-0004** | §1.1 role set (as canonical names, §2); §1.2 multi-role; §1.3 self-role-change ban; §2 permission-plus-ownership and server-side enforcement; §2 mapping-editing UI out of scope for v1; §4.1 AllStudents Phase 1 scope; §4.3 conflict of interest; §7.3/§8 Administrator separation from student data; §10 enforcement rules | The empty Student column of §2's mapping table is now an explicit per-phase strategy (§7-§8, §14), not a statement that Students never hold permissions; §2's "mapping is seeded" now means per-phase incremental seeding | §2's delegation rule ("actor cannot grant … a permission … that the actor does not hold") — §11; §2's wholesale-seeding reading — §7; the `Forms.Manage`/`Workflows.Manage` = GraduateStaff rows — §13 (Administrator owns definition management at the owning phases) |
| **ADR-0015** | §5 authentication-path independence; §6 Students SSO-only; §2 UserKind-driven local-login eligibility; the principle that authentication never grants authorization | Its "user type" matrix language binds to UserKind for eligibility and Role for authorization (§1, §4) | — |
| **ADR-0016** | §3 bootstrap outcome (UserKind=Administrator + Administrator role); §4.2 bootstrap completion guard (bootstrap-specific, per its own text); §10.1 UC2 dependency; §12 audit-event approach and Phase 1A/1C sequencing | §4.2's role-holder test remains only the bootstrap guard; §18 of this ADR is the general effective-Administrator definition it deferred | — |

The deferrals ADR-0016 §10.2/§11 listed for this ADR are resolved here:
canonical role naming (§2), canonical permission naming (§5), Student
mapping strategy (§7-§8, §14-§15), delegation semantics (§11), UserKind ↔
Role consistency (§4), and the general last-Administrator semantics (§17-§19).
Older ADR files are **not** edited; per the README convention, supersession
is recorded here (full supersession would additionally mark the old status
line; these are partial supersessions, so no status lines change).

## Alternatives considered

| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Keep ADR-0004's delegation rule | Tighter on paper | Makes in-scope role assignment impossible under ADR-0004's own Administrator mapping; conflates capability with permission possession | Superseded with a redefined escalation set (§11) |
| Implicit "Administrator has all permissions" (super-admin) | Simple mental model | Violates ADR-0004 §8 separation and least privilege; no Super Admin concept exists in any document; hands the admin plane student data | Rejected (§6, §12) |
| Permission wildcards / role hierarchy (`Forms.*`, `Administrator > GraduateStaff`) | Less seeding | Hidden authority; catalog examples would become runtime semantics; hard to audit effective grants | Rejected (§6); flat, explicit model kept |
| Wholesale seeding of ADR-0004's full table in Phase 1A | One big seed | Seeds capabilities with no enforcing feature; freezes granularity before Phases 2-5; risks accidental capability leakage | Rejected in favor of incremental strategy (§7) |
| lowercase.dot as canonical runtime naming | Matches ARCHITECTURE §10 examples | Contradicts the Accepted ADR-0004 table that owns the only mapping; two conventions would coexist in claims/policies | Rejected; PascalCase.Dot canonical (§5), lowercase stays illustrative |
| Persisted, per-user assignable StaffScope in UC2 | Future-proof | No per-staff scoping exists in Phase 1 (ADR-0004 §4.1); premature persistence invites scope-editing UIs and audit surface with no requirement | Rejected; scope derived from role semantics in Phase 1 (§12/StaffScope rule below) |
| `Roles.Grant` / `Roles.Revoke` split | Finer granularity | No operational need identified in v1; doubles the capability surface and tests | Rejected; single `Roles.Assign` (§10) |
| `Roles.Manage` as the Phase 1A name | Familiar name | Implies role-definition/mapping management that v1 does not provide | Rejected; name must match enforced behavior (§10) |
| Introduce `Students.EditOwnProfile` | Symmetry with staff edit | No authoritative source; contradicts ADR-0004 §3.2 | Rejected (§15) |

## StaffScope (Phase 1)

For Phase 1:

```text
GraduateStaff   → StaffScope = AllStudents
Student         → StaffScope = None
Administrator   → StaffScope = None
```

There is no per-department, per-faculty or per-staff student scope in Phase 1
(ADR-0004 §4.1). Administrator MUST NOT be modeled as
`Administrator = GraduateStaff + AllStudents`: the system-administration
authority must not accidentally grant student-data access (§12). StaffScope
is an authorization/data-access boundary, not an authentication category
(§1). For Phase 1, the effective StaffScope may be derived from authorization
context/role semantics rather than persisted as an independently assignable
privilege; UC2 introduces no user-configurable StaffScope. A future Accepted
ADR may supersede this when department/faculty scoping is introduced
(ADR-0004 §4.4's extension point).

## Consequences

- UC2 can be implemented without guessing: roles, names, the exact Phase 1A
  seed set, mapping, scope values, fail-closed rules, audit events and
  invariants are all specified.
- Positive: the Administrator plane becomes functional (role assignment
  possible under §11) while remaining separated from student data (§12);
  every deferral ADR-0016 handed to this ADR is closed.
- Negative/limits: `GraduateStaff` loses the `Forms.Manage`/`Workflows.Manage`
  mapping rows when Phases 2/4 land (§13); illustrative lowercase examples
  remain in other documents until owning slices update them (§5, by design —
  no broad rewrite); `Students.EditProfile` remains unscheduled (§15) — a
  known ROADMAP gap this ADR deliberately does not paper over.
- The delegation change (§11) widens what a holder of `Roles.Assign` can do
  relative to ADR-0004's original text; the mitigation is the redefined
  escalation set plus §17-§20 invariants plus audit (§22).
- Follow-up work: UC2 (role store + migration, `Users.Manage`/`Roles.Assign`
  constants, idempotent seeds per §8, `PermissionPolicyProvider`/`HasPermission`
  machinery, `ICurrentUser.StaffScope` per the StaffScope rule, authorization
  matrix + escalation + fake-scope tests) → UC3 (admin UI enforcing §9-§11,
  §17-§20) → later phases extend seeds incrementally (§7, §14).
- Documents changed by this decision: `docs/decisions/README.md` (index),
  `AGENTS.md` §24 and `ROADMAP.md` (Phase 1A gate + canonical seed
  wording). ARCHITECTURE §10's illustrative lowercase list is intentionally
  not rewritten here.

## Security impact

- Least privilege is strengthened: no implicit Administrator-all (§6), no
  user-configurable scope (StaffScope rule), no direct user permissions (§6),
  and no wildcard semantics (§6) — every effective permission is an explicit,
  seeded, reviewable row.
- The one deliberate widening (§11 delegation) is bounded by: supported roles
  only, capability-gated (`Roles.Assign`), invariant-guarded (§17-§20),
  audit-covered (§22) and fail-closed (§21). Assigning a role never transfers
  its permissions to the assigner.
- The Administrator plane cannot reach student data implicitly (§12) and has
  StaffScope None, closing the "admin accidentally scoped to all students"
  path.
- Fail-closed rules (§21) prevent UserKind/AuthenticationMethod shortcuts and
  unknown-permission grants; NotFound preservation prevents enumeration.
- No new runtime surface is created by this ADR itself (documentation only);
  the enforcement surface arrives with UC2/UC3 under these rules.

## Migration / rollout impact

- **No schema change from this ADR.** UC2 adds the Identity role store
  (role/user-role/role-claim tables) in its own reviewed migration, and its
  seeds insert exactly the §8 set, idempotently (AGENTS §17).
- No configuration keys, no secrets, no package changes.
- Ordering: ADR-0017 (this document) Accepted → UC2 → bootstrap slice
  (ADR-0016, already ordered after UC2's role infrastructure) → UC3 →
  owning phases extend the seed (§7).
- Rollback: removing seeds/rows is not required for rollback of this ADR
  itself; it changes no deployed behavior until UC2 implements it.

## Acceptance criteria

1. The canonical persisted role names are exactly `Student`, `GraduateStaff`,
   `Administrator`; `Staff` never appears as a persisted role name.
2. Multi-role membership is supported; no code or document introduced by this
   ADR assumes a single role; the conflict-of-interest self-decision
   prohibition (§3) remains enforced by ownership checks.
3. No authorization decision reads `UserKind` or `AuthenticationMethod`
   (§1, §4, §21); UserKind/Role inconsistency fails closed for privileges not
   represented by actual roles.
4. PascalCase.Dot is the canonical permission naming; no second runtime
   naming convention exists in constants, seeds or policies (§5).
5. Permissions are assigned to roles only; direct user permissions, role
   hierarchy, wildcards and implicit Administrator-all do not exist (§6).
6. The Phase 1A seed contains exactly three roles and exactly two
   permissions (`Users.Manage`, `Roles.Assign`), mapped only to
   Administrator; Student and GraduateStaff have empty Phase-1A permission
   sets; none of `Settings.Manage`, `Audit.View`, `Forms.*`, `Students.*`,
   `Submissions.*`, `Workflows.*`, `Reports.*`, `Documents.*` is seeded in
   Phase 1A (§8).
7. `Roles.Assign` grants and revokes supported roles as one capability; no
   `Roles.Grant`/`Roles.Revoke`/`Roles.Manage` runtime permissions exist in
   v1 (§10).
8. An Administrator holding `Roles.Assign` can assign `GraduateStaff` without
   holding GraduateStaff's permissions, and such an assignment does not grant
   the Administrator those permissions (§11).
9. Escalation protection under the new model is enforced: supported roles
   only, no direct permission grants, no arbitrary role creation/editing,
   capability required, last-Administrator and self-role-change invariants
   enforced, no authentication-path/UserKind bypass (§11).
10. Administrator receives no student-data permission implicitly and has
    StaffScope None; GraduateStaff has AllStudents; Student has None (§12,
    StaffScope rule).
11. `Forms.Manage`/`Workflows.Manage` (canonical) are Administrator-owned at
    their owning phases, are not seeded in Phase 1A, and neither implies
    `Submissions.View` (§13).
12. `Students.EditOwnProfile` does not exist; `Students.EditProfile` is
    deferred and not seeded in Phase 1A (§15).
13. Resource access composes Permission+Ownership or Permission+StaffScope as
    documented; out-of-ownership/out-of-scope keeps NotFound where required;
    Administrator does not bypass these by role (§16).
14. The last-Administrator invariant is operation-agnostic (§17); effective
    Administrators are counted by `Administrator` role membership per §18;
    temporary lockout never permits removal of the last Administrator; two
    concurrent operations cannot jointly leave zero effective Administrators
    (§19).
15. An Administrator cannot change their own roles through the normal
    workflow (§20).
16. Authorization fails closed per §21 (Forbidden for missing permission; no
    grant for unknown permissions; UI is UX only; server-side authoritative).
17. Conceptual audit events are named per §22; Phase 1A audit remains
    best-effort and no claim of transactional persistence is made; no
    secrets/credentials appear in audit.
18. ADR-0016's listed deferrals are all resolved; ADR-0004's partial
    supersessions are recorded in §23; no older ADR file was edited.
19. This task changed no production code, tests, migrations, database,
    packages or runtime configuration.
