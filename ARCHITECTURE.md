# ARCHITECTURE.md

Companion documents: `AGENTS.md` (rules), `DESIGN.md` (UI), `OPENCODE-MASTER-INSTRUCTIONS.md` (agent workflow), `ROADMAP.md`, `docs/decisions/` (ADRs). All five documents form one system. `AGENTS.md` defines the implementation constraints and wins on rules; this document defines concepts and flows. Clearly obsolete, duplicated or superseded text may be reconciled once the intended decision is established from the surrounding documents. An unresolved business/domain contradiction that the documents do not settle must be reported as `REQUIRES DECISION`, never guessed. Security requirements are never weakened to remove a contradiction.

## 1. System context

**Purpose:** replace paper-based graduate-study forms with electronic forms, submissions, documents, approvals, reporting and an audit trail.

| Actor | Goals |
|---|---|
| Student | See eligible forms, fill drafts, upload documents, submit, track status, correct returned requests |
| Graduate Studies Staff | Review queue, inspect submissions/attachments, approve/reject/return with comments, search students, run reports (within their scope) |
| Administrator | Design/publish forms, assign forms, define workflow revisions, manage users/roles/permissions/reference data, view audit |

External: University Student API (profile source; may be unavailable). The authentication provider is decided in ADR-0001.

```text
Student ─┐                                  ┌─▶ SQL Server
Staff  ──┼─▶ GraduateRecords.Web (MVC) ─────┼─▶ File storage (outside wwwroot)
Admin  ──┘                                  └─▶ University Student API (resilient HTTP)
```

## 2. Architectural style

**Modular Monolith + Lightweight Clean Architecture + Feature-Based Application Layer.**

- **Monolith:** one deployable, one database. Small team, internal load, and transactional consistency among submission, workflow, projection and audit.
- **Modular:** modules (Students, Forms, Submissions, Workflows, Documents, Reports, Administration, Auditing) are folders with clear ownership, not services.
- **Lightweight Clean Architecture:** four projects, one-way dependencies, a framework-free Domain.
- **Feature-based Application:** code grouped by use case (`Features/Submissions/SubmitFinal/`).
- **Intentionally not used:** microservices, SPA/React/Blazor, event sourcing, BPMN engine, generic low-code platform, heavy CQRS, MediatR, AutoMapper, generic repositories/custom Unit of Work, message brokers, SignalR/real-time, job frameworks.

## 3. Project boundaries

```text
Web ───────────▶ Application ───▶ Domain
 │                    ▲              ▲
 └─(§3.2 only)▶ Infrastructure ─────┴──── (Infrastructure → Application, Domain)
```

### 3.1 Application ↔ EF Core (decided)

- Application may depend on **EF Core abstractions** (`DbSet`, `IQueryable`, `Include`, `AsNoTracking`, `IDbContextTransaction`).
- `IApplicationDbContext` is the application-facing persistence abstraction: `DbSet<T>` per domain entity, `SaveChangesAsync`, `BeginTransactionAsync`. Identity tables are not exposed.
- SQL Server/provider-specific behavior (JSON functions, raw SQL, app locks) belongs to Infrastructure behind ports (`IReportQueries`).
- No repository-per-entity, no generic repository, no custom Unit of Work. `DbContext` is the unit of work.
- Ports exist only at real boundaries: `ICurrentUser`, `IClock`, `IAuditWriter`, `IFileStorage`, `IFileInspector`, `IStudentDirectory`, `IReportQueries`, `IIdentityAdministration`, `IApplicationDbContext`.

### 3.2 Web → Infrastructure (narrow exception)

Allowed only for (a) composition in `Program.cs` and (b) Identity authentication plumbing in `AccountController` (`SignInManager`). Business use cases never bypass Application. Admin user/role/permission management goes through the Application port `IIdentityAdministration` (justified because Identity types must stay out of Application). No other identity abstraction exists. Enforced by architecture tests.

### 3.3 Repository layout (baseline)

```text
GraduateRecords/
├── AGENTS.md  ARCHITECTURE.md  DESIGN.md  ROADMAP.md  OPENCODE-MASTER-INSTRUCTIONS.md  README.md
├── GraduateRecords.sln  global.json  Directory.Build.props  Directory.Packages.props  .editorconfig
├── docs/decisions/            # README.md, ADR-TEMPLATE.md, ADR-NNNN-*.md
├── docs/university-api/       # contract notes + anonymised sample payloads only
├── src/
│   ├── GraduateRecords.Domain/          Common/ Students/ Forms/ Submissions/ Workflows/ Documents/ Auditing/
│   ├── GraduateRecords.Application/     Common/{Abstractions,Handlers,Results,Security} Features/<Module>/<UseCase>/
│   ├── GraduateRecords.Infrastructure/  Persistence/{Configurations,Interceptors,Migrations,Seed} Identity/ UniversityApi/
│   │                                    Files/ Auditing/ Reporting/ Jobs/ Time/ DependencyInjection.cs
│   └── GraduateRecords.Web/             Program.cs Areas/{Student,Staff,Admin}/{Controllers,Models,Views}
│                                        Controllers/ Authorization/ Middleware/ UI/{TagHelpers,ViewComponents,Extensions}
│                                        Views/Shared/{Ui,Components,DynamicForm} Assets/css/app.css package.json
│                                        wwwroot/{css(generated),js/{core,components,features},fonts/vazirmatn,icons}
└── tests/  GraduateRecords.{Domain,Application,Integration}Tests/   (Integration includes Architecture/)
```

`Jobs/` contains exactly `UniversitySyncHostedService` and `DocumentCleanupHostedService`. `Students/` in Domain holds `Student`, reference entities (per ADR-0012), `NationalId`, `AcademicStatus`, `SyncStatus`, `SyncErrorCategory`. `Workflows/` holds `WorkflowDefinition`, `WorkflowDefinitionRevision`, `WorkflowStepDefinition`, `WorkflowInstance`, `WorkflowStepInstance`, `WorkflowEvent`.

## 4. Core domain concepts

| Concept | Key facts |
|---|---|
| **Student** | Local copy of the university profile: `StudentNumber` (unique), names (as received) + `NormalizedFullName`, `NationalId`, Faculty/Department/Program refs, `EntryYear`, `AcademicStatus`, `UserId`, sync metadata (`LastSyncedAt`, `LastSyncAttemptAt`, `LastSyncStatus`, `LastSyncError` category). |
| **Reference data** | `Faculty`, `Department`, `AcademicProgram`: local read model. Source of truth is decided by ADR-0012 (§12). No `Professor` entity in v1. |
| **FormDefinition** | Stable identity of a form (code, title, category, active flag). |
| **FormVersion** | `VersionNumber`, status `Draft/Published/Retired`, `ContentHash`, optional (nullable) `WorkflowDefinitionId` (introduced in Phase 4), `RowVersion`. Published = immutable. |
| **FormField** | `Key`, `Type`, `Label`, `HelpText`, `IsRequired`, `SortOrder`, `ValidationJson`, `OptionsJson`, `AutoFillSource?`, `AutoFillMode?`, `IsReportable`, `IsSensitive`. |
| **FormAssignment / AssignmentRule** | Definition + window (`OpensAt`, `ClosesAt`) + max submissions per student + OR-combined rules. |
| **FormSubmission** | Student, pinned `FormVersionId`, `Status`, current mutable `DataJson`, `ProfileSyncedAt`, `RowVersion`. |
| **SubmissionRevision** | Immutable snapshot at final submit/resubmit. |
| **SubmissionFieldValue** | Derived reporting projection (rebuildable). |
| **Document** | Generic metadata only (secure identity, ownership/upload metadata); bytes live in file storage. No submission/field references. |
| **WorkflowDefinition** | Stable identity of a workflow. |
| **WorkflowDefinitionRevision** | Versioned, immutable-when-published template; owns `WorkflowStepDefinition`s. |
| **WorkflowInstance / WorkflowStepInstance** | Runtime for one submission: revision id + full snapshot, round, status, claim, `RowVersion`. |
| **WorkflowEvent** | Append-only timeline (`ActorUserId` + `ActorDisplayName` snapshot, from/to, comment, `OperationId`). |
| **Audit** | Append-only `AuditLog`. |

## 5. Dynamic forms

Controlled, not general-purpose.

### 5.1 Lifecycle and immutability
```text
FormDefinition
 ├─ v1 [Published] ◀─ submissions A, B pinned
 ├─ v2 [Published] ◀─ submission C pinned
 └─ v3 [Draft]     ◀─ only editable version, at most one
Publish(v3): validate → ContentHash → Published → (v2 may be Retired)
```
Editing = new Draft cloned from the latest published (keys preserved). Publishing locks it. Enforcement: domain guards + `ImmutablePublishedFormInterceptor` + no update handler + `ContentHash` integrity check.

`ContentHash` is a deterministic SHA-256 hash of the canonical publishable form-definition content, including `WorkflowDefinitionId` (serialized as null when unbound, in every phase). It proves the published definition has not changed. `WorkflowDefinitionId` is immutable on a Published version. Changing it requires a new Draft version, which gets its own `ContentHash` when published.

### 5.2 Retirement
A `Retired` version cannot start new submissions. Existing pinned submissions continue (drafts, saves, final submit, resubmit, workflow). Retirement never invalidates in-progress work. Only `StartSubmission` checks the status.

### 5.3 Field definitions (v1)
Types: `Text, Textarea, Number, Date, Select, Radio, Checkbox (boolean), File, StudentInfo`. `Key` is stable and ASCII. `OptionsJson` = list of `{value (stable generated code), label}`. `ValidationJson` is structured per type. No conditions, formulas, repeating groups, scripts or free-form regex.

### 5.4 AutoFill
`AutoFillSource` (closed enum) → `StudentAutoFillMapper` (switch in code). `AutoFillMode` per field:

| Mode | Behavior | Persisted in `DataJson`? |
|---|---|---|
| DisplayOnly | Read-only, shown from the current Student profile | No |
| InitialValue | Server pre-fills; student may edit; the edited text is the answer | Yes (as an ordinary answer) |
| Snapshot | Read-only; server writes the profile value at every save/submit; records `ProfileSyncedAt` | Yes (trusted snapshot) |

```text
InitialValue:  profile value → pre-filled initial value → student may edit → final answer (validated as ordinary text)
Snapshot:      trusted Student record → server writes on each save/submit (client value ignored)
DisplayOnly:   trusted Student record → rendered only
```
Combinations: `StudentInfo` → DisplayOnly | Snapshot; `Text` → InitialValue | Snapshot. The server never trusts a client-supplied AutoFill value.

### 5.5 National ID
`IsSensitive`, masked in UI/lists, excluded from default exports, never in audit payloads, logs or projections. Default mode `DisplayOnly` (not copied into `DataJson`). `Snapshot` is allowed only when a historical copy is truly required: the field must be `IsSensitive` and the designer needs `forms.sensitive-snapshot`; the publish dialog warns. `InitialValue` is not allowed.

### 5.6 Validation
`DynamicSubmissionValidator` iterates the pinned version's fields: required, type parsing (Persian-digit and Jalali aware), min/max, named patterns, option membership, file references. Draft save = lenient. Final submit = strict. Unknown keys are discarded.

### 5.7 DataJson
Flat object by field key (text → string, number → number, date → `"yyyy-MM-dd"`, checkbox → bool, select/radio → option code, file → document GUID array).
```json
{ "thesis_title": "بررسی ...", "defense_date": "2026-09-14", "consent": true, "attachments": ["3f2b…guid"] }
```
Answers are posted as `answers[<key>]`. The binder produces `Dictionary<string,string[]>`. The handler parses by field type. File fields carry document GUIDs that the server verifies (Document and its submission-owned association) belong to this student's submission.

### 5.8 ProfessorSelector (deferred to Phase 6)
Not v1 because no confirmed business requirement and no confirmed data source exist. If ADR-0013 approves it, the ADR must define: data source (university API vs local list), who may be selected (department/faculty scope, active only), server-side validation of the selected id against that scope, storage as `{id, name}` (id + display snapshot), and behavior when professor data later changes (the snapshot in submitted data is kept; the live record is used only for new selections; inactive professors remain valid for already-pinned drafts). Until then, a `Text` field captures names.

## 6. Form assignment (v1)

Rule dimensions: `All, Faculty, Department, Program, EntryYear, AcademicStatus, Student`. A student is eligible if ANY rule matches (OR), the window is open, the limit is not reached, the version is Published (not Retired), and eligibility policy per ADR-0005 passes. Evaluated server-side in `GetAvailableForms` and re-checked in `StartSubmission` only. Retirement and status are checked when *starting* work; an existing submission pinned to a later-Retired version keeps working (editing drafts, saving, final submit, resubmit, workflow) per AGENTS §7 and ARCHITECTURE §5.2, so final submit never re-checks the form version's Published status. AND/NOT groups are Phase 6.

## 7. Submissions, revisions, projection

| Concept | Semantics |
|---|---|
| `FormSubmission.DataJson` | Current mutable state. Draft saves/autosave overwrite it. |
| `SubmissionRevision` | Immutable snapshot created ONLY on final submit and resubmit. |
| `SubmissionFieldValue` | Derived, rebuildable projection. Not authoritative. |

```text
Draft ─(autosave: DataJson only, no revision)→ Draft
  ↓ Final Submit
SubmissionRevision #1  (+ projection written, same transaction)
  ↓ Return for Correction            (workflow-enabled forms only)
student edits current DataJson (autosave allowed, no revision)
  ↓ Resubmit
SubmissionRevision #2  (+ projection replaced, same transaction)
```

## 8. Workflow

### 8.1 Definition → revision → runtime
```text
WorkflowDefinition
 └─ WorkflowDefinitionRevision r3 [Published, immutable]   (r4 [Draft] being edited)
      └─ WorkflowStepDefinition(s)
             │ StartWorkflow (at final submit)
             ▼
WorkflowInstance { WorkflowDefinitionRevisionId = r3, snapshot: name, revision no., round }
 └─ WorkflowStepInstance { snapshot: name, order, RequiredPermission; status; ClaimedBy/At }
```
Both are part of the in-flight model: the revision id gives provenance and reporting, and the snapshot makes runtime independent of any definition table. The runtime never reads definition/revision tables for an in-flight instance. A published revision can never be edited. Changes create a new Draft revision that affects only future instances. A `Retired` revision starts no new instances. In-flight instances continue.

**Workflow is optional per form version.** `FormVersion.WorkflowDefinitionId` is nullable (column and designer binding control arrive in Phase 4). Publishing a form version never requires a Published revision. If it is null, final submit ends in `Submitted` (no `WorkflowInstance`). If it is set, final submit requires a Published revision, resolves the latest one, and snapshots it. Forms are therefore usable in Phases 2–3 without Phase 4. Changing the binding means a new Draft form version and a new publish.

### 8.2 State machine
```text
Draft
  ↓ Submit
Submitted
  ↓ Review (claim)
UnderReview
 ├── Approve ─────▶ Approved        (terminal)
 ├── Reject ──────▶ Rejected        (terminal in v1)
 └── Return ──────▶ NeedsCorrection
                         ↓ Resubmit (new SubmissionRevision, Round+1)
                      Submitted
```
Rules: comment mandatory on Reject/Return; actor ≠ owner; actor holds the snapshotted step permission; every action writes a `WorkflowEvent` and an audit entry in the same transaction; idempotent via `OperationId`; `RowVersion` concurrency. v1 revisions have exactly one review step (`MaxStepsV1 = 1`). `StepOrder` is stored so multi-step sequential routing (Phase 6) needs no schema change. No parallel/conditional routing, no BPMN.

The state machine above applies to workflow-enabled forms. A no-workflow form is exactly Draft → Submitted in v1, with no Cancel, Close, Approve, Reject, Review or NeedsCorrection. Any such behavior needs a separate feature and ADR. No-workflow submissions never enter the review queue, which is workflow-only. Staff reach them only through search, student profile and reports, under existing rules (`submissions.view-any` + staff scope; SR4 audit when opening).

### 8.3 Claiming (minimal; ADR-0014)
`Review` sets `ClaimedByUserId`/`ClaimedAtUtc` on the step instance (`Submitted → UnderReview`). Decisions require the claimer. `Release` (claimer, or `submissions.reassign`) returns to `Submitted`. No task-management subsystem. If ADR-0014 removes claiming, delete the two columns and `Release`.

### 8.4 Idempotency
`OperationId` is issued by the server when the form/dialog is rendered, so it exists **before** the command executes. It is stored on `SubmissionRevision` and `WorkflowEvent` with unique indexes. Uniqueness: `SubmissionId + OperationId` and `WorkflowInstanceId + OperationId`. A replayed request returns the original result with no new write. A different request against changed state returns `Conflict`. `RowVersion` handles races. A unique-violation from a racing duplicate is resolved as a replay. Applies to final submit, resubmit, review, release, approve, reject, return. No generic framework.

## 9. Student synchronization

**Three separate concerns:** authentication (Identity), profile synchronization (`Student` freshness), form eligibility (Application policy).

```text
Sign-in (Identity)  ─────────────▶ session established    (never calls the university API)
Dashboard / start form
   └─ freshness(student)           age = now − LastSyncedAt
        Fresh      age <  FreshHours                       → nothing
        Stale      FreshHours ≤ age < StaleDays            → bounded inline sync; silent on failure
        VeryStale  StaleDays ≤ age < ExpiredDays           → attempt + banner with last-sync date
        Expired    age ≥ ExpiredDays                       → attempt; eligibility effect per ADR-0005 (until decided: banner only)
UniversitySyncHostedService → batches, same resilience rules, retried next cycle
```
Thresholds are configuration/ADR values. A student never synced successfully is `Expired`.

- `LastSyncedAt` = last success. `LastSyncAttemptAt` supports retry suppression (5 min default). `LastSyncStatus` ∈ `Succeeded|NotFound|Unavailable|InvalidData|Unauthorized|RateLimited`. `LastSyncError` ∈ `None|Timeout|Unavailable|Unauthorized|RateLimited|InvalidPayload|NotFound|Unknown` (category only: no secrets, stack traces, tokens or raw payloads).
- **Integrity rule:** invalid or incomplete university data never overwrites valid local data. Steps: validate payload → verify required identity fields (student number matches; National ID valid; identity conflict with an existing local National ID ⇒ `InvalidPayload`, keep local, audit `Student.SyncIdentityConflict`) → reject invalid payloads wholesale → preserve existing data (missing optional fields don't null values) → record the failure.
- A student with no local row needs a successful lookup (identity proofing is not skipped).
- If authentication is delegated to a university IdP that is down, sign-in cannot be rescued without weakening security. Mitigate with sliding sessions only. No offline credential bypass.
- **Resilience:** typed client, attempt timeout 3 s, total 8 s, 2 retries (exponential + jitter) on 5xx/408/429/network, circuit breaker (e.g., 50 % over ≥ 5 calls, 30 s break), cancellation propagated, structured logs (status, duration, retry count, correlation id; no PII).
- Operational endpoints show university-API state as `Degraded`, never `Unhealthy`.

## 10. Authorization

```text
Identity user → Role → Permission (role claim "permission") → Policy (per permission)
                                   +
                   Resource ownership  +  Staff scope  (inside handlers)
```

Example permissions: `students.view`, `students.view-national-id`, `students.sync`, `referencedata.manage` (only if ADR-0012 allows local edits), `forms.view`, `forms.manage`, `forms.publish`, `forms.assign`, `forms.sensitive-snapshot`, `submissions.create`, `submissions.view-own`, `submissions.view-any`, `submissions.review`, `submissions.approve`, `submissions.reject`, `submissions.return`, `submissions.reassign`, `documents.download-any`, `workflows.manage`, `workflows.publish`, `reports.view`, `reports.export`, `reports.backfill`, `users.manage`, `roles.manage`, `audit.view`.

- Controller attribute gates the action type. The handler gates the resource: ownership for students, scope for staff, and the snapshotted step permission for workflow actions.
- **Staff scope** (ADR-0004): `ICurrentUser.StaffScope` + `ScopeExtensions.WithinScope/CanAccess` used by every staff query and by-id handler from day one. The default resolves to `All` until the ADR is Accepted. A test with a restrictive fake scope is mandatory. Out-of-scope ⇒ NotFound.
- Student vs another's resource ⇒ NotFound. Missing permission ⇒ Forbidden. `SecurityStamp` revalidation (≤ 5 min) makes revocation effective.

## 11. Audit

Append-only `AuditLog`; production DB login has no UPDATE/DELETE. Business-change audit rows are saved in the same transaction as the change.

**Auditable events:** login success/failure/lockout/logout; role/permission/user changes; form created/draft saved (summary)/published/retired; assignment changes; workflow revision published; submission created/submitted/resubmitted; review started/released; approved/rejected/returned; document uploaded/downloaded; reference-data changes; manual sync, sync identity conflict; report exported; projection backfill/rebuild.

`AuditLog` and `WorkflowEvent` store `ActorUserId` (stable reference) and `ActorDisplayName` (snapshot at event time). Historical rendering uses the snapshot. Application never queries Identity for it.

**Sensitive reads (fail-closed):** SR1 National ID reveal; SR2 staff full-profile view; SR3 document download/preview; SR4 staff opening another student's submission content; SR5 audit-log viewing and exports. **Not every read is sensitive.** Ordinary reads and ordinary operational events (e.g., login) are best-effort: failure is logged via Serilog and the request proceeds. An audit outage must not take down the whole application.
Never in audit/logs: passwords, tokens, secrets, full National ID, full `DataJson`, file contents, raw stack traces, raw API payloads.

## 12. Reference data

Faculty, Department and AcademicProgram are a **local read model**. Source of truth is decided by ADR-0012:
- If the university API is authoritative: records are synchronized (same validation/integrity rules as student sync: bad payloads never overwrite valid data), keyed by external code. Local edits are limited to explicit, documented overrides (e.g., an `IsActive`/display-order flag stored in separate local columns, never overwritten by sync). No CRUD UI otherwise.
- If manual: admin CRUD under `referencedata.manage`, audited.
No `Professor` management exists in v1 (§5.8).

## 13. File storage

```text
{FileStorage:RootPath}/{yyyy}/{MM}/{guid}.bin   outside wwwroot, ACL: app identity only
Upload:   POST /documents (auth, antiforgery header) → validate → IFileInspector → store → Document(Active)
Download: GET /documents/{id} → ownership/scope/permission → SR3 audit → stream (attachment, nosniff, no-store)
```
Documents are **permanently generic**. Phase 1C builds storage, metadata (secure identity + generic ownership/upload metadata), authorized download and cleanup. `Document` never holds `SubmissionId`, `FieldKey` or `FormFieldId` and has no dependency on the Submissions/Forms modules. In Phase 3 the Submissions module owns the association `SubmissionId + FieldKey + DocumentId` (a submission-owned entity/table; naming is an implementation detail), along with per-field caps and final reference verification. **Upload ≠ Submit:** the physical file is uploaded before final submission. Final submit verifies each referenced Document and its association (ownership/context, Active/usable, field validity, count/type/size) and finalizes the linkage transactionally with the submission. It never re-uploads.

Validation: size cap, extension allow-list, magic-byte match, no path input, no double extensions. `IFileInspector` is a no-op antivirus hook. No scanner in v1.

`DocumentCleanupHostedService` removes only explicitly cleanup-eligible incomplete/abandoned upload artifacts per ADR-0010 and its age thresholds. Non-Active status alone never makes a Document eligible. This does not mean a non-Active state (e.g., Rejected) can never be cleaned up: ADR-0010 may explicitly make a particular state cleanup-eligible after a defined age threshold. Eligibility always comes from an explicit ADR-0010 rule. Phase 3 adds eligibility for Documents with no submission-owned association beyond the orphan threshold (currently >24 h, subject to ADR-0010).

## 14. Reporting

1. **Relational columns**: status, form version, snapshot ids, dates, workflow status. Used for all common filtering.
2. **`SubmissionFieldValue`**: derived projection of `IsReportable` fields from the pinned version; written transactionally at submit/resubmit; replaced on resubmit; rebuildable from `DataJson`; never authoritative; never contains sensitive fields.
3. **`JSON_VALUE`/`OPENJSON`**: rare ad-hoc queries, Infrastructure only.

Not done: per-form tables, per-form columns, dynamically created computed columns. A computed column is allowed only for a fixed system-level key shared by all forms (none planned).
**Reportability and versions:** a submission follows the `IsReportable` flags of the version it was submitted against. A later version flagging a key reportable does not touch old submissions. Making history reportable is an explicit, audited, bounded **backfill** command (`reports.backfill`, `Source = Backfill`). A **rebuild** command repairs the projection for already-reportable fields. Neither runs automatically.
Exports: Excel via ClosedXML (row cap, formula-injection neutralization, sensitive fields excluded by default, SR5 audit). PDF via an ADR-chosen library after a Persian/RTL spike. Index and optimize only from measured slow queries.

## 15. Security boundaries

Browser↔Web: HTTPS, cookie flags, antiforgery, CSP, rate limits. Web↔Application: ViewModels → explicit commands; identity from `ICurrentUser` only. Application↔data: ownership + scope + permission in handlers; parameterized EF; audit. Application↔files: `IFileStorage` + `IFileInspector` only. Application↔university API: `IStudentDirectory` only; payload validation; no secrets in logs. Client-side: untrusted-text rule (`textContent`, DOM APIs; no `innerHTML` for dynamic text). Admin plane: separate area, explicit permissions, audited, last-admin protection. Errors: global handler with correlation id. Config: secrets outside the repo; `ValidateOnStart`; durable Data Protection keys.

## 16. Persian requirements

- `<html lang="fa" dir="rtl">`, Vazirmatn self-hosted, Tailwind logical utilities.
- **Jalali:** UI input/display Jalali; one `JalaliDate` helper; storage `date`/UTC `datetime2`; never Jalali strings in the database; week starts Saturday.
- **National ID:** 10 digits, checksum, not all-same digits; ASCII storage.
- **Canonical vs display:** identifiers, numbers, dates, search/comparison keys are canonicalized (ASCII digits, `ي→ی`, `ك→ک`). Free-text answers, names as received, and filenames keep original characters (only trim, NFC, control/bidi-override stripping; ZWNJ kept). Searchable fields keep both original and normalized values.
- **Mixed direction:** LTR data in `dir="ltr"` controls aligned to the end; names in sentences in `<bdi>`; exports set RTL direction.
- UI digits: Persian digits in display; identifier inputs accept both.
- Time: store UTC via `IClock`, display Asia/Tehran; date-only values use `date`.

## 17. Observability and operations

Serilog (structured, no PII), `CorrelationId` middleware, global exception handler (user text: `خطایی رخ داده است.` / `کد پیگیری: <id>`), health endpoints (`/health/live`, `/health/ready`; no sensitive detail; anonymous only as deployment health checks require), two hosted services (scoped `DbContext` per run, cancellation-aware, bounded batches, no overlap, graceful shutdown). Hosting, proxy/IIS, HTTPS, SQL Server version, database and file-store backups, Data Protection key persistence, log retention and deployment strategy are ADRs (0006, 0007, 0008, 0010) accepted before the slice that depends on them (AGENTS.md §24).

## 18. Frontend baseline

Tailwind **v4** (4.x; CSS-first config in `Assets/css/app.css`; `@tailwindcss/cli`; exact version pinned via lockfile; browser support confirmed in ADR-0011). Vanilla ES modules. No jQuery/React. Details in `DESIGN.md`.

## 19. Future extension

| Category | Items |
|---|---|
| **Supported by Phase 5** | Roles/permissions; 9 field types; versioned forms; OR assignment; AutoFill (3 modes); draft/final submit; revisions + projection; optional single-step workflow with immutable revisions + snapshot, claim, idempotent actions; audit; secure generic files; resilient student sync; search; reports; Excel/PDF |
| **Designed for later (Phase 6, each its own ADR + tests + rollout notes)** | AND/NOT assignment; multi-step sequential/advanced workflow; notifications via a port; additional integrations; university SSO (if not chosen earlier); antivirus via `IFileInspector`; admin 2FA; `CheckboxGroup`; `ProfessorSelector` + professor sync; conditional visibility via predefined operators (no expressions); lifecycle behavior for no-workflow submissions (cancel/close) |
| **Out of scope** | Microservices; SPA/React/Blazor; event sourcing; BPMN; low-code platform; formulas/scripts/repeating groups; multi-tenancy; admin-supplied regex; public self-registration; SignalR |

## 20. Configuration summary

`ConnectionStrings:Default`, `FileStorage:{RootPath,MaxBytes,AllowedExtensions}`, `UniversityApi:{BaseUrl,credentials,AttemptTimeout,TotalTimeout,RetryCount}`, `StudentSync:{FreshHours,StaleDays,ExpiredDays,InlineBackoffMinutes,ExpiredBlocksEligibility}`, `Identity:{Lockout,Password,CookieLifetime}`, `Serilog`, `Jobs:{SyncBatchSize,SyncIntervalMinutes,CleanupIntervalMinutes}`. Secrets via user-secrets/environment. Options validated on start.
