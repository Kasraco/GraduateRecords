# ADR-0004: Staff data scope and authorization boundaries

- **Status:** Accepted
- **Date:** 2026-10-18
- **Deciders:** Project Owner
- **Related documents/ADRs:** ADR-0001 (authentication), ADR-0002 (provisioning and
  activation), future ADR-0003 (Sama integration and synchronization), future ADR on
  Roles/Permissions implementation

## Context
The system has exactly three actors: Student, Graduate Studies Staff ("Staff") and
Administrator. Professors are not users of the system.

Staff work with all graduate students of the university and need to search and view
student profiles even when no request exists. Staff may edit profile data and export
data to Excel/PDF. The Administrator manages the system itself (users, roles,
settings) and must not see student data or documents.

Student profile data is synchronized from Sama (ADR-0002/ADR-0003), so edits made by
staff must coexist with synchronization.

## Decision

### 1. Roles
1.1 Roles: `Student`, `GraduateStaff`, `Administrator`.
1.2 One person may hold several roles (ADR-0002: one user per National ID). Every
    role grant and revoke is audited.
1.3 An Administrator cannot change their own roles. The last active Administrator
    cannot be deactivated (ADR-0002).
1.4 Professors have no accounts and no access. Professor information needed for the
    `ProfessorSelector` field is read from Sama as reference data and stored as a
    reference value, not as a user.

### 2. Permissions
Authorization uses roles that carry permissions, enforced through ASP.NET Core
authorization policies. Permissions are code constants. The default role-to-permission
mapping is seeded into Identity role claims (`permission` claim type).
An admin UI to edit the permission-to-role mapping is out of scope for the first
release; the default mapping is seeded. Assigning roles to users is in scope
(ROADMAP Phase 1A), and any such operation must enforce AGENTS §13: an actor cannot
grant a permission (or a role carrying it) that the actor does not hold.

| Permission | Student | GraduateStaff | Administrator |
|---|---|---|---|
| `Students.View` (search, list, profile) | - | yes | - |
| `Students.EditProfile` | - | yes | - |
| `Submissions.View` (any student's submissions and attachments) | - | yes | - |
| `Submissions.Decide` (review, approve, reject, return for correction) | - | yes | - |
| `Forms.Manage` (Form Designer, versions, assignments) | - | yes | - |
| `Workflows.Manage` (workflow definitions) | - | yes | - |
| `Reports.View` | - | yes | - |
| `Reports.Export` (Excel/PDF) | - | yes | limited, see 7.3 |
| `Users.Manage` (create staff, activate/deactivate, roles) | - | - | yes |
| `Settings.Manage` (e.g. `StudentStatusPolicy`) | - | - | yes |
| `Audit.View` | - | - | yes |

Student actions (own profile view, own submissions, drafts, submit, resubmit) still
carry a controller permission attribute per AGENTS §13 (`[HasPermission(...)]`), and
are additionally governed by the ownership rules in section 3. Authorization is
always permission **plus** resource ownership; ownership alone is not sufficient and
permission alone is not sufficient. Student-owned endpoints must be covered by the
authorization matrix test (student-owner allowed, student-other denied).

Note: `Forms.Manage` and `Workflows.Manage` are assigned to GraduateStaff because they
concern forms and processes, not student data. See "Assumptions to confirm".

### 3. Student data access
3.1 A Student can access only their own data. The student identity is always resolved
    from the authenticated user on the server. Student ids, national IDs or student
    numbers sent by the client are never used to select whose data to load.
3.2 Students cannot edit their profile in the first release.
3.3 Any access to another person's record by a Student is an authorization failure
    and is audited as a security event.

### 4. Staff scope
4.1 Staff scope is all students of the university. There is no per-department,
    per-faculty or per-staff scoping in the first release.
4.2 Any staff member may review any submission. There is no per-step assignee and no
    reviewer assignment in the first release.
4.3 Conflict of interest: a user cannot decide on a submission that belongs to
    themselves (applies if a person holds both Staff and Student roles).
4.4 All staff-side student queries go through Application query handlers, never
    directly from controllers to EF, so a scope filter can later be added in one place
    without changing controllers or views.
4.5 There is no temporary delegation or substitution.

### 5. Student fields visible to staff
Staff-facing DTOs expose only the following fields (explicit allowlist). The full Sama
`Student`/`Person` object is never mapped to a staff view model.

| Field | Source | Staff may edit |
|---|---|---|
| Student number (owner term: «شماره دستیاری», to be confirmed) | Sama | no |
| National ID | Sama / identity key | no |
| First name, last name | Sama | yes |
| Father name | Sama | yes |
| ID-card number (شماره شناسنامه) | Sama | yes |
| Birth place, birth date | Sama | yes |
| ID issue place (محل صدور) | Sama | yes |
| Gender | Sama | yes |
| Marital status | Sama | yes |
| Nationality, religion | Sama | yes |
| Number of children | local only (not in Sama) | yes |
| Email | Sama contact info | yes |
| Landline phone | Sama contact info | yes |
| Mobile phone | Sama contact info | yes (see 6.5) |

5.1 Fields not in this table (for example GPA, grades, financial data, military status,
    passport number, photo) are not exposed to staff in the first release. Adding a
    field requires updating this ADR.
5.2 Academic data (faculty, course of study, level, entry term, Sama status, dates)
    shown in student context comes from Sama and is read-only.
5.3 In list/search results the National ID is shown masked (first 3 and last 2 digits
    hidden except where needed). The full National ID is shown only on the profile page.
    Staff may search by full National ID, student number or name.

### 6. Profile editing and synchronization
6.1 `Students.EditProfile` allows editing only fields marked editable in section 5.
    `NationalId` and student number are never editable because they are identity keys.
6.2 Input is normalized (Persian/Arabic digits and characters), validated per field
    (e-mail format, mobile format `09xxxxxxxxx`, Jalali date input converted to
    canonical Gregorian date) and bound through dedicated edit view models
    (no mass assignment of entities).
6.3 A locally edited field is recorded in `StudentProfileOverride`
    (student, field name, edited by, edited at). Synchronization from Sama
    never overwrites a field that has an override.
6.4 Every edit writes a `StudentProfileRevision` row (student, field, old value, new
    value, edited by, edited at). Revision rows hold personal data, so they are readable
    only with `Students.View` and are not copied into the general audit log. The audit
    log receives only an event naming the student id and the field names changed.
6.5 Changing the mobile number resets `PhoneVerifiedAt`. The student is Pending again
    and must verify the new number by OTP at next sign-in (ADR-0002). SSO is still
    required, so the change does not bypass authentication.
6.6 Edits use optimistic concurrency (row version) so two staff members do not
    silently overwrite each other.

### 7. Export
7.1 `Reports.Export` allows Excel and PDF export of data that the exporting user may
    view. Exports contain only the allowlisted fields (section 5) and submission data
    permitted by `Submissions.View`.
7.2 Every export is audited: actor, report type, filter summary, row count, time.
    No exported data is written into the audit log.
7.3 The Administrator may export only administrative data (user list, audit log). The
    Administrator cannot export student profiles, submissions or documents.
7.4 Excel export must neutralize formula injection: cells starting with `=`, `+`, `-`,
    `@` or tab/CR are prefixed with an apostrophe or otherwise escaped.
7.5 Exported files are generated on demand, streamed through an authorized endpoint
    and not kept on disk.

### 8. Administrator boundary
8.1 The Administrator manages users, roles/permissions mapping, and settings. They have
    no access to student profiles, submissions or attachments.
8.2 The user management screen shows only account data: name, masked National ID,
    role, kind, active state, eligibility, phone-verified state. It does not link to
    student profile pages.
8.3 The Administrator can view the audit log (`Audit.View`). National IDs in audit views
    are masked; students are referenced by internal id.

### 9. Audit
Events written for this ADR (no passwords, tokens, OTP codes or unnecessary personal
payloads):
- Student profile viewed (every staff view, actor id, student id, time).
- Student search performed (actor, filter types used, result count; no raw National ID
  or free-text values).
- Student profile edited (actor, student id, field names).
- Submission viewed, attachment downloaded (actor, submission id, file id).
- Decision taken: approve, reject, return for correction.
- Export performed (see 7.2).
- Role granted/revoked, user activated/deactivated (ADR-0002).
- Authorization denied: Student accessing another student's data, any user
  requesting a resource outside their permissions.
Views are logged per profile open without deduplication. Noise is accepted over the
risk of missing access events.

### 10. Enforcement rules
10.1 All access rules above are enforced server-side with policies and ownership
     checks in query/command handlers. Hidden UI elements are never authorization.
10.2 Every endpoint serving student data, submissions, attachments or exports has an
     explicit `[Authorize(Policy = ...)]`. Controllers without an explicit policy are a
     build/review failure, and a default fallback policy requires an authenticated user.
10.3 Responses for resources the user may not access return the same result as for
     non-existent ones (404/403 policy decided in the Web layer) to avoid leaking
     existence by id guessing.
10.4 Attachment downloads always pass through an authorized endpoint with the
     permission and ownership checks of this ADR.

## Alternatives considered
| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Full access to everything for all staff | Simplest | Exposes fields not needed for the job; breach risk | Field allowlist limits exposure |
| Scoping by faculty/department for staff | Least privilege | No such organizational need today; more complexity | Staff serve the whole university; extension point kept (4.4) |
| Administrator can see student data | Easier support | Violates separation of duties | Owner decision: admin manages system only |
| Staff edits overwritten by next sync | Simple | Silent loss of edits | Override flags (6.3) |
| Read-only staff profile | Safer | Owner requires editing | Editing allowed with history and audit |
| Role-only checks without permissions | Simple | Harder to evolve | Requirement: roles + permissions + policies |

## Consequences
- New entities: `StudentProfileOverride`, `StudentProfileRevision`, plus role-claim
  seed data for permissions.
- Sync code (ADR-0003) must honour overrides and never touch identity keys.
- Dedicated staff and student view models and queries are required; no entity or Sama
  object is returned to views.
- The audit table will grow quickly because every profile view is logged. Plan an
  index on (actor, time) and (student, time) and a retention/archival policy.
- Because all staff see all fields in section 5 and can export them, audit review of
  export and view events is the main control against misuse.

## Acceptance criteria
- A Student cannot open another student's profile, submission or attachment by changing
  an id in the URL.
- An Administrator cannot reach any student profile, submission, attachment or student
  export (HTTP-level test, not only hidden UI).
- A Staff user sees only the allowlisted fields; Sama-only fields (GPA, military status,
  passport, grades) never appear in staff responses.
- Editing `NationalId` or student number is impossible, including by crafted POST
  (over-posting test).
- A staff edit survives the next synchronization from Sama; a Sama-only field is updated.
- Changing a student's mobile number sets the user Pending until OTP verification.
- Every profile view, search, edit, export and decision creates an audit event without
  personal payloads.
- Excel export neutralizes cells starting with `=`, `+`, `-`, `@`.
- A user cannot decide on their own submission.
- Concurrent edits of the same profile do not silently overwrite each other.
- Endpoints without an explicit authorization policy do not exist (verified by test).
