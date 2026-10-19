# AGENTS.md: Mandatory Rules for AI Coding Agents

Project: **Graduate Student Electronic Records System** (internal university application, Persian/RTL-first).
Read this file completely before changing anything. Then read `ARCHITECTURE.md`. Read `DESIGN.md` before UI work. Read `docs/decisions/` for Accepted ADRs.
These rules override your defaults. If a task conflicts with them, STOP and ask. Never deviate silently.
Baseline status: this document, `ARCHITECTURE.md`, `DESIGN.md`, `OPENCODE-MASTER-INSTRUCTIONS.md` and `ROADMAP.md` form ONE system. `AGENTS.md` defines the implementation constraints and wins on rules; `ARCHITECTURE.md` defines concepts and flows. Clearly obsolete, duplicated or superseded text may be reconciled once the intended decision is established from the surrounding documents. An unresolved business/domain contradiction that the documents do not settle must be reported as `REQUIRES DECISION`, never guessed. Security requirements are never weakened to remove a contradiction.

## 0. Locked stack

.NET 10 · ASP.NET Core MVC + Razor · EF Core · SQL Server · ASP.NET Core Identity · **Tailwind CSS v4 (4.x, CSS-first configuration)** · vanilla JavaScript (ES modules) · Persian/RTL-first.
Architecture: **Modular Monolith + Lightweight Clean Architecture + Feature-Based Application Layer.** Four production projects: Domain, Application, Infrastructure, Web.

## 1. Must NOT be introduced without an Accepted ADR (`docs/decisions/ADR-NNNN-*.md`)

- React, Next.js, Blazor, Vue, Angular, any SPA/client framework, htmx, Alpine.js, jQuery (including jquery-validation-unobtrusive), SignalR/real-time features.
- Microservices, message brokers, extra deployable services, extra production projects.
- Event Sourcing, domain-event infrastructure, outbox, sagas, BPMN/workflow engines (Elsa, Workflow Core, etc.).
- MediatR, AutoMapper, Mapster, any mediator/pipeline framework.
- Generic repository, repository-per-entity, `IRepository<T>`, custom `IUnitOfWork`, specification libraries.
- Heavy CQRS (separate stores, projections for everything, event buses).
- Hangfire, Quartz or any job framework.
- New NuGet/npm packages outside §20. An ADR must state the concrete need and the license.
- CDN-hosted scripts, styles, fonts or icons. Everything is self-hosted.
- Arbitrary expressions in form configuration (C#, JS, SQL, formulas, `eval`, free-form regex).
- Raw HTML from admins or users rendered to others.
- Switching the Tailwind major version, adding a `tailwind.config.js`, or copying v3-era snippets (see §21).
- Form features outside the v1 scope in §7 (conditional visibility, calculated fields, repeating sections, scripting).

## 2. Project boundaries and dependencies

| Project | Contains | May reference | MUST NOT |
|---|---|---|---|
| Domain | Entities, value objects, enums, invariants, state-transition methods, pure helpers (`PersianTextNormalizer`, `JalaliDate`, `NationalId`) | nothing | EF Core, ASP.NET, Identity, HTTP, file system, `DateTime.UtcNow`, `ILogger` |
| Application | Use cases (command/query/handler/validator/DTO), ports, `Result`, permission constants | Domain; `Microsoft.EntityFrameworkCore` (abstractions only); FluentValidation | SQL Server provider, `EF.Functions.*` SQL Server extensions, raw SQL, ASP.NET Core MVC/HttpContext, Identity types, `HttpClient` implementations, file system |
| Infrastructure | `AppDbContext`, EF configurations, migrations, Identity, university API client, file storage, audit writer, SQL Server-specific report queries, hosted services | Application, Domain | Business rules/decisions; direct calls from controllers |
| Web | MVC areas, controllers, ViewModels, Razor, TagHelpers, ViewComponents, middleware, authorization attributes/handlers, JS/CSS | Application; Infrastructure only per §2.2 | `AppDbContext`, entities in views, Infrastructure services in controllers |

Dependency direction: `Web → Application → Domain`; `Infrastructure → Application + Domain`.

### 2.1 Application ↔ EF Core boundary (decided, do not revisit)

- Application MAY depend on EF Core abstractions (`DbSet<T>`, `IQueryable<T>`, LINQ, `Include`, `AsNoTracking`, `IDbContextTransaction`).
- `IApplicationDbContext` is THE application-facing persistence abstraction. It exposes `DbSet<T>` for domain entities, `SaveChangesAsync`, and `BeginTransactionAsync`. It does NOT expose Identity tables.
- SQL Server/provider-specific behavior (`JSON_VALUE`, `OPENJSON`, raw SQL, `sp_getapplock`, index hints) lives only in Infrastructure, behind ports such as `IReportQueries`.
- FORBIDDEN: repository-per-entity, generic repository, custom Unit of Work, "query objects" whose only purpose is to wrap `DbSet`. `DbContext` is the unit of work.

### 2.2 Web → Infrastructure exception (narrow)

`Web → Infrastructure` is allowed ONLY for:
1. composition in `Program.cs` (`AddInfrastructure(...)`, middleware registration);
2. ASP.NET Identity authentication plumbing inside `Controllers/AccountController` (`SignInManager<ApplicationUser>`: sign-in, sign-out, lockout, password change/reset, external login callback).

Everything else goes through Application handlers. Business use cases must NEVER bypass Application and call Infrastructure directly. Admin user/role/permission management goes through the Application port `IIdentityAdministration` (create/disable user, assign roles, set role permissions), implemented in Infrastructure. This port is justified because Identity types must not enter Application. Do not add `IIdentityService` or other identity wrappers.
`ApplicationUser`/`ApplicationRole` live in Infrastructure. `Student.UserId` is a plain id, never a navigation to `ApplicationUser`.
Architecture tests enforce all of §2. Never weaken them to make code compile.

## 3. Feature-based organization

- Application code: `Features/<Module>/<UseCase>/` holding `XCommand|XQuery`, `XHandler`, `XValidator` (if input rules exist), result/DTO types.
- No root folders named `Services`, `Managers`, `Helpers`, `Utils`, `Repositories`. Put code next to its use case. Share only after a second real use.
- Modules: Students, Forms, Submissions, Workflows, Documents, Reports, Administration, Auditing. A module may read another module's entities through `IApplicationDbContext`. It must NOT call another module's handlers. Shared logic goes to Domain methods or `Common/`.
- Web is organized by audience (Areas: Student, Staff, Admin).

## 4. Handlers, CQRS-lite, results

- Handler = plain class implementing `ICommandHandler<TCommand,TResult>` / `IQueryHandler<TQuery,TResult>` with `Task<Result<TResult>> HandleAsync(TCommand, CancellationToken)`. Registered by assembly scan. Controllers inject the handler interface.
- Commands change state. Queries do not (except audit writes for classified sensitive reads, §14).
- Expected failures return `Result` with `Error{Kind, Code, Message}` (`Validation, NotFound, Forbidden, Conflict, Unavailable`). Exceptions are for unexpected failures only.
- Cross-cutting steps (validation, authorization/ownership/scope check, audit, transaction) are called explicitly inside handlers. Assembly scanning for handler/validator registration is allowed. A reflection-based business-behavior or pipeline framework is not.
- Queries use `AsNoTracking()` and `Select(...)` into DTOs. Never return entities to Web. Mapping is manual.

## 5. Controller rules

- Thin: bind ViewModel → build command → call handler → map `Result`. Target ≤ 15 lines per action. No business logic, EF, file I/O, `HttpClient`.
- Global filters: `AuthorizeFilter` (authenticated by default) and `AutoValidateAntiforgeryToken`. State changes are POST/PUT/DELETE only. Post-Redirect-Get after success, with a toast via TempData.
- `[AllowAnonymous]` only on login, error, health (`/health/live`, `/health/ready`) and static infrastructure endpoints. This is not permission to add any other anonymous application endpoint.
- ViewModels are separate from entities, DTOs and commands. Never bind entities or commands from the request (over-posting).
- `ModelState` handles input shape. Business rules are enforced in Application always.
- Result mapping: `NotFound`→404, `Forbidden`→403, `Validation`→re-render with errors, `Conflict`→conflict message with reload option, `Unavailable`→friendly message.

## 6. Domain rules

- Entities protect invariants through methods (`FormVersion.Publish`, `WorkflowInstance.Approve`). No public setters on rule-bearing state. No domain events, no specification pattern, no generic aggregate framework.
- Time is passed into domain methods (from `IClock`). No `DateTime.UtcNow` in Domain/Application.
- Value objects only where they carry validation (`NationalId`).

## 7. Dynamic forms (non-negotiable)

- `FormDefinition` (identity) → `FormVersion` (`Draft | Published | Retired`) → `FormField`. `FormAssignment` + `AssignmentRule` decide who may fill a definition. `FormSubmission` is pinned to one `FormVersionId` at draft creation and never moves.
- **Published versions are immutable.** Changing a published form creates a new Draft cloned from it. Enforced in three layers: domain guard methods, no update handler for published rows, and `ImmutablePublishedFormInterceptor` (only `Published → Retired` is allowed). At most one Draft per definition.
- **ContentHash.** Publishing computes `ContentHash`: a deterministic SHA-256 hash of the canonical publishable form-definition content, **including `WorkflowDefinitionId`**. It proves the published definition content has not changed. It makes no claim about database row bytes.
- **Retirement:** a `Retired` version cannot start new submissions. Existing submissions pinned to it keep working (editing drafts, saving, final submit, resubmit, workflow processing). Retirement never invalidates anything already started. Only "start new" checks the status.
- **Workflow binding is optional.** `FormVersion.WorkflowDefinitionId` is nullable. A form version may be published without a workflow. Publishing a form version NEVER requires a Published `WorkflowDefinitionRevision`. See §10 for submission behavior. `WorkflowDefinitionId` is part of the hashed content, so it can never change on a Published version. To change the binding, create a new Draft version, then publish it (it gets its own `ContentHash`). The canonical serialization ALWAYS includes the workflow-binding key (null when unbound), from Phase 2 onward. This keeps Phase 2 hashes verifiable after the column arrives in the Phase 4 migration.
- `FormField.Key`: `^[a-z][a-z0-9_]{1,63}$`, unique per version, immutable once the field exists in a published version (so reports can span versions). Changing a field's type means a new key. Labels are display only. Answers are keyed by `Key`.
- **v1 field types (closed enum):** `Text, Textarea, Number, Date, Select, Radio, Checkbox, File, StudentInfo`. `Checkbox` = single boolean. **`ProfessorSelector` is NOT in v1** (Phase 6, requires ADR-0013 and the rules in ARCHITECTURE.md §5.8). Supervisor-style answers use `Text` in v1. Adding a type requires approval and an ARCHITECTURE.md update.
- Validation config is structured data: required, min/max length, min/max value, date range, named patterns from a fixed list (`NationalId, Mobile, Email, DigitsOnly`), file types/max size/max count. No free-form regex.
- The server accepts only keys defined in the pinned version. Unknown keys are discarded (debug log), never stored.
- Out of scope in v1, do not add: conditional visibility, calculated fields, repeating groups, cross-field rules, scripting, drag-and-drop requirement.

## 8. Submissions, DataJson, revisions, reporting projection

Three concepts with distinct semantics:

| Concept | Semantics |
|---|---|
| `FormSubmission.DataJson` | **Current mutable state** of the submission's answers. Autosave and draft saves overwrite it. It is the authoritative current answer store. |
| `SubmissionRevision` | **Immutable snapshot**, created ONLY on final submit and on resubmit: `RevisionNumber`, copy of `DataJson`, `FormVersionId`, `ProfileSyncedAt`, submitter, time, `OperationId`. Never created by autosave or draft save. Never updated. |
| `SubmissionFieldValue` | **Derived, rebuildable reporting projection**. NOT authoritative. Written in the same transaction as final submit/resubmit. |

Lifecycle: `Draft → (final submit) → Revision #1 → (return) → student edits current DataJson → (resubmit) → Revision #2`. (Return/resubmit applies to workflow-enabled forms only, see §10.)

- `DataJson`: `nvarchar(max)`, `ISJSON` check constraint, flat object `{ "<fieldKey>": <value> }`. Values: text → string; number → JSON number; date → `"yyyy-MM-dd"` Gregorian; checkbox → bool; select/radio → option code string; file → array of document GUIDs. Serialize/parse only through `SubmissionDataSerializer` (`System.Text.Json`).
- Reviewers read the latest revision. Earlier revisions remain viewable. Nothing is overwritten.
- **Projection rules:**
  - Fields flagged `IsReportable` in the submission's pinned `FormVersion` are projected into `SubmissionFieldValue(SubmissionId, FormVersionId, RevisionNumber, FieldKey, ValueText, ValueNumber, ValueDate, ValueBool, Source)`. On resubmit, rows for that submission are replaced in the same transaction.
  - A historical submission follows the reportability flags of the version it was submitted against. A later version flagging a key reportable does NOT retroactively project old submissions.
  - Making historical data reportable is an explicit **backfill operation** (command `BackfillFieldProjection`, permission `reports.backfill`, parameters: definition, field key, version range). It is audited, batch-bounded, idempotent, and writes `Source = Backfill`. A projection rebuild for already-reportable fields is a separate explicit `RebuildFieldProjection` command. Neither runs automatically.
  - `IsSensitive` fields are never projected. `IsReportable` and `IsSensitive` cannot both be true (publish validation).
- Reporting tiers: relational columns → `SubmissionFieldValue` → `JSON_VALUE/OPENJSON` for rare ad-hoc queries (Infrastructure only). **No per-form tables, no per-form columns, no dynamically created computed columns.** Never load all `DataJson` into memory to filter.

## 9. AutoFill and sensitive data

- `AutoFillSource` (closed enum): `FirstName, LastName, FullName, StudentNumber, NationalId, Faculty, Department, Program, EntryYear`. Mapped in code (`StudentAutoFillMapper`, a switch over the enum). No reflection by string, no expressions.
- `AutoFillMode` (closed enum), chosen per field in the designer:
  1. **DisplayOnly**: rendered read-only from the current `Student` profile. NOT written to `DataJson`. No answer key exists for it.
  2. **InitialValue**: server pre-fills the answer (at `StartSubmission`, and at render if the key is absent). The student may edit. The final edited text is a normal answer in `DataJson`, validated like any answer.
  3. **Snapshot**: read-only for the student. The server writes the profile value into `DataJson` on every draft save and on final submit, recomputed from the trusted `Student` record. Records `ProfileSyncedAt`.
- Allowed combinations: `StudentInfo` → DisplayOnly | Snapshot. `Text` with a source → InitialValue | Snapshot. All other field types: no AutoFill.
- The server is always authoritative. Client-supplied values for DisplayOnly/Snapshot fields are ignored. A client-supplied value for an InitialValue field is treated as an ordinary student answer, never as a trusted profile value.
- **National ID** is sensitive:
  - Default mode is `DisplayOnly` (shown from the profile, masked unless viewer has `students.view-national-id`). It is NOT persisted to `DataJson` merely because it is an AutoFill source.
  - `Snapshot` is allowed only if the form genuinely needs a historical copy. The field must be `IsSensitive`. Configuring it requires permission `forms.sensitive-snapshot`. The publish dialog must warn. `InitialValue` is not allowed for `NationalId`.
  - A sensitive field's value: masked in lists, never projected, excluded from default exports, never in audit payloads or logs, shown unmasked only to holders of `students.view-national-id`, with audit.

## 10. Workflow rules

- Model: `WorkflowDefinition` → `WorkflowDefinitionRevision` (`Draft | Published | Retired`, owns `WorkflowStepDefinition`s, has `ContentHash`) → `WorkflowInstance` → `WorkflowStepInstance`, plus `WorkflowEvent`.
- **A published `WorkflowDefinitionRevision` is immutable.** Changing a workflow creates a new Draft revision (copy of the latest). Same three-layer enforcement as form versions (domain guards, no update path, interceptor).
- **Workflow is optional per form version.** `FormVersion.WorkflowDefinitionId` is nullable and points to a `WorkflowDefinition` (never to a revision). The binding column and the binding control are introduced in Phase 4.
  - `WorkflowDefinitionId == null`: final submit ends in `Submitted`. No `WorkflowInstance` is created.
  - `WorkflowDefinitionId != null`: final submit requires a Published `WorkflowDefinitionRevision`. The handler resolves the definition's **latest Published revision** and snapshots it into a new `WorkflowInstance`. If none exists, final submit fails with `Unavailable` and the problem is logged.
  - Publishing a form version never requires a Published revision, and Phase 2/3 never depend on Phase 4.
- **In-flight model = revision reference + runtime snapshot (both mandatory):**
  - `WorkflowInstance.WorkflowDefinitionRevisionId` records which exact revision started it (provenance, reporting).
  - At start, copy definition name, revision number, and every step (name, order, required permission) into `WorkflowInstance`/`WorkflowStepInstance` columns in the same transaction as the submit.
  - Runtime code (claim, approve, reject, return, resubmit, permission checks, UI) must NEVER read `WorkflowDefinition`, `WorkflowDefinitionRevision` or `WorkflowStepDefinition` for an in-flight instance.
- **Retirement:** a `Retired` revision cannot start new instances. In-flight instances continue unaffected.
- **v1 limit:** a revision has exactly ONE review step (`WorkflowLimits.MaxStepsV1 = 1`, validated at publish). Step rows carry `StepOrder` so multi-step sequential workflows (Phase 6) need no schema change. No parallel branches, no conditional routing, no BPMN.
- **State machine (Domain, with a transition-table unit test):**
  ```
  Draft --Submit--> Submitted --Review--> UnderReview
  UnderReview --Approve--> Approved (terminal)
  UnderReview --Reject--> Rejected (terminal in v1)
  UnderReview --ReturnForCorrection--> NeedsCorrection
  NeedsCorrection --Resubmit--> Submitted
  ```
  Only `NeedsCorrection` is editable/resubmittable after first submit. `Rejected` and `Approved` are terminal.
  The diagram applies to workflow-enabled forms only. A no-workflow form is exactly `Draft --Submit--> Submitted` in v1. There is NO Cancel, Close, Approve, Reject, Review, NeedsCorrection or Resubmit for a no-workflow submission, and it is read-only for the student after submit. Any such behavior requires a separate feature and ADR.
- Comment is mandatory (non-blank) for Reject and ReturnForCorrection. The actor must not be the submission's owner. The actor must hold the **snapshotted** step's `RequiredPermission` in addition to the action permission on the controller (`submissions.approve|reject|return|review`).
- Every action appends a `WorkflowEvent` (`ActorUserId`, `ActorDisplayName`, time, from/to status, comment, `OperationId`). `ActorUserId` is the stable identity reference. `ActorDisplayName` is the display-name snapshot at event time. History is always rendered from the stored snapshot. Application code must not query Identity to reconstruct historical names.
- `FormSubmission.Status` is a denormalized copy of the workflow status, updated in the same transaction by the same handler. For workflow-enabled submissions the `WorkflowInstance` is authoritative after first submit. For no-workflow submissions `Status` stays `Submitted`.
- No-workflow submissions never enter the review queue (workflow-only). Staff reach them through search, student profile and reports under existing rules (`submissions.view-any` + staff scope; SR4 audit when opening).
- **Claiming (minimal, pending ADR-0014):** `Review` = "start review". It sets `ClaimedByUserId` and `ClaimedAtUtc` on the step instance and moves `Submitted → UnderReview`. Decisions require the claimer. `Release` (by claimer, or by a holder of `submissions.reassign`) returns `UnderReview → Submitted` and clears the claim. No task-management subsystem, no assignment queues. If ADR-0014 rules out claiming, drop the two columns and `Release`. `Review` then stays as an explicit start-review action with no claim.

## 11. Idempotency and concurrency

`RowVersion` alone is NOT sufficient. Required for: final submit, resubmit, approve, reject, return, review/release, and any future state-changing workflow command.

- The server generates an `OperationId` (GUID) when it renders the page/dialog that performs the action. **It must exist before the business command executes.** It is carried in the request and is never generated by the handler or derived from the result entity. It is an idempotency key only (never an authority) and is scoped to (entity, actor).
- Uniqueness is `SubmissionId + OperationId` and `WorkflowInstanceId + OperationId`. A replay returns the original successful outcome and creates no duplicate business state, revision, event or audit entry. Persist it where the effect is recorded: `SubmissionRevision.OperationId` and `WorkflowEvent.OperationId`, each with a unique index (scoped per entity).
- Handler algorithm: (1) load entity; (2) if an effect with this `OperationId` already exists → return the original success (replay, no new write, no duplicate audit); (3) otherwise validate expected state (wrong state → `Conflict`); (4) apply; (5) `SaveChanges` with `RowVersion` check; a unique-index violation or concurrency exception caused by a racing duplicate is resolved by re-checking (2).
- Draft save/autosave is last-write-wins guarded by `RowVersion`. A conflict (two tabs) returns `Conflict` and the UI offers reload. No idempotency key is needed. Uploads are not idempotent. Duplicates become orphans handled by cleanup.
- UI additionally disables the button on submit. That is UX only.
- Do not build a generic idempotency framework or middleware.

## 12. Student synchronization and API resilience

- University API access only through `IStudentDirectory` (Application port), implemented in `Infrastructure/UniversityApi` with a typed `HttpClient` + `Microsoft.Extensions.Http.Resilience`. Contract defined by ADR-0003.
- Required: attempt timeout (default 3 s), total timeout (8 s), max 2 retries with exponential backoff + jitter on transient failures only (5xx, 408, 429 with capped `Retry-After`, network errors), circuit breaker, `CancellationToken` always propagated. No infinite retry. No retry on other 4xx.
- `StudentSyncResult`: `Succeeded | NotFound | Unavailable | InvalidData | Unauthorized | RateLimited`. The directory never throws for expected failures and never leaks exception text.
- **Authentication, profile synchronization and form eligibility are three separate concerns.** Authentication code must not call `IStudentDirectory`. A sync failure never blocks sign-in or access of a known student. Sync runs after authentication (bounded inline attempt + background job).
- Persisted on `Student`: `LastSyncedAt` (last SUCCESS), `LastSyncAttemptAt`, `LastSyncStatus`, `LastSyncError`. **`LastSyncError` is a category enum** (`None, Timeout, Unavailable, Unauthorized, RateLimited, InvalidPayload, NotFound, Unknown`), never raw exception text. Never store secrets, tokens, stack traces, or raw API payloads in any sync field, log or audit.
- **Integrity rule (critical): invalid or incomplete university data must never overwrite valid local student data.** The sync process must: (1) validate payload shape, lengths, enum mapping; (2) verify required identity fields (`StudentNumber` equals the requested one; `NationalId` passes `NationalId` validation; and if a local `NationalId` already exists and differs → treat as `InvalidPayload` identity conflict, keep local data, write audit `Student.SyncIdentityConflict`); (3) reject invalid payloads wholesale (no partial apply); (4) preserve existing data (a missing optional field never nulls an existing value); (5) record `LastSyncAttemptAt`, status, and category.
- After a failed attempt, skip inline attempts for that student for `StudentSync:InlineBackoffMinutes` (default 5).
- Staleness ladder, with `age = now − LastSyncedAt` (values from configuration/ADR-0005; options validated on start so that `FreshHours < StaleDays·24 < ExpiredDays·24`): `Fresh`: age < FreshHours · `Stale`: age ≥ FreshHours and < StaleDays · `VeryStale`: age ≥ StaleDays and < ExpiredDays · `Expired`: age ≥ ExpiredDays. A student never synced successfully is `Expired`.
- What `Expired` BLOCKS is decided by ADR-0005. Until then the only allowed behavior is a banner. Eligibility (form start, final submit) is evaluated separately in Application from `AcademicStatus` and ADR-0005. It is never inferred from API availability alone.
- A student with no local row (first provisioning) requires a successful directory lookup. Identity proofing is never skipped.
- API credentials come from secret configuration. Logs contain ids, status codes, durations and retry counts, never PII or payloads.

## 13. Authentication and authorization

- ASP.NET Core Identity cookie authentication (model per ADR-0001/0002). Lockout enabled, password policy in `Infrastructure/Identity`, cookies `HttpOnly`, `Secure`, `SameSite=Lax`, sliding expiration with absolute cap, `SecurityStamp` revalidation ≤ 5 min.
- Chain: Identity user → Role → Permission (role claim type `permission`) → Policy. Permissions are constants in `Application/Common/Security/Permissions.cs`. Controllers use `[HasPermission(Permissions.X)]`, backed by `PermissionPolicyProvider`/`PermissionHandler`. Never hard-code role names for security decisions.
- **Authorization = Permission + Resource ownership + Staff scope**, all server-side:
  1. permission attribute on the action;
  2. in the handler: ownership (a student may touch only their own submission/document/profile);
  3. in the handler: **staff scope**, via `ICurrentUser.StaffScope` and the shared extensions in `Application/Common/Security/ScopeExtensions.cs` (`WithinScope(...)` for queries and `CanAccess(...)` for by-id checks).
- Staff scope policy is decided by ADR-0004. Until it is Accepted the default `StaffScope` resolves to `All`, but **every staff-facing query and every staff by-id handler MUST already call the scope extensions**, so changing the policy touches one place. A test with a restrictive fake scope must prove filtering.
- Student → other student's resource, or staff → out-of-scope resource: return **NotFound**. Authenticated but missing permission: **Forbidden**.
- Hidden UI is never authorization. Never trust ids, roles, status, version ids, owner ids, or step ids from the request.
- Admin operations (user/role/permission changes, publishing, reference data) require explicit permissions, are audited, and cannot grant a permission the actor lacks. The last active Administrator cannot be removed/disabled.
- No custom password hashing, no home-made tokens, no authorization logic in JS.

## 14. Audit

- `IAuditWriter` writes to the append-only `AuditLog` (no update/delete code paths; the production DB login has INSERT/SELECT only).
- Fields: `OccurredAtUtc, ActorUserId, ActorDisplayName, Action, EntityType, EntityId, Result (Success|Denied|Failed), Summary, DetailsJson (allow-listed), IpAddress, UserAgent (truncated), CorrelationId`.
- `ActorUserId` = stable identity reference. `ActorDisplayName` = display-name snapshot at event time. Audit viewing renders from the snapshot. Application code must not query ASP.NET Identity to rebuild historical actor names, and `ActorUserId` is never removed.
- Action names are constants in `Domain/Auditing/AuditActions.cs`.
- **Business-state changes:** the audit row is added to the same `DbContext` and saved in the same transaction as the change.
- **Not every read is sensitive.** Classified sensitive reads (fail-closed: if the audit write fails, the read fails):
  - **SR1** National ID reveal (unmasked display or export).
  - **SR2** Staff viewing a student's full profile page (`Student.ProfileViewed`).
  - **SR3** Document download or inline preview (`Document.Downloaded`).
  - **SR4** Staff opening a submission's answers/attachments (review page, revision view) for a submission they do not own.
  - **SR5** Audit log viewing and report/data exports.
  Everything else (own dashboard, own submissions, form catalog, lists, search result pages) is NOT fail-closed. Ordinary operational events (login success/failure/lockout, list/search access) are best-effort: on audit failure, log an error via Serilog and continue. An audit outage must never take the whole application down. Adding a new fail-closed category requires updating this list and ARCHITECTURE.md §11.
- Audit/log content must never include: passwords, tokens, secrets, full National ID, full `DataJson`, file contents, raw stack traces, raw API payloads. Store ids and changed field names, not values, unless the value is non-sensitive and allow-listed.
- Every new command and every new sensitive read must declare its audit action in the completion report.

## 15. Files

- Storage root: `FileStorage:RootPath` (absolute, outside `wwwroot`, never a static-file path). Storage name = server-generated GUID, layout `{root}/{yyyy}/{MM}/{guid}.bin`. No user-provided path or extension.
- Generic file infrastructure (`IFileStorage`, `IFileInspector`, `Document` metadata, authorized download endpoint, cleanup job) is **Phase 1C**.
- The Documents infrastructure is **permanently generic**. The `Document` entity, the upload endpoint, `IFileStorage` and the download pipeline never contain or depend on `SubmissionId`, `FieldKey`, `FormFieldId`, or anything in the Submissions/Forms modules, in any phase. The business association `SubmissionId + FieldKey + DocumentId`, per-field limits and final reference verification are owned by the **Submissions module** (Phase 3), as a submission-owned association entity/table (exact naming is an implementation detail). Creating it never moves `SubmissionId` or `FieldKey` into `Document`, and the generic upload/storage code stays unchanged and reusable.
- `Document` metadata: id, owner student, uploader, original name (display only), detected content type, size, SHA-256, status, uploaded-at, and generic ownership/upload metadata (owner student, uploader, upload time). Nothing about submissions or fields is stored on `Document`.
- Validate on upload: authenticated + permitted; size ≤ max (default 10 MB); extension allow-list (default `pdf, jpg, jpeg, png`); **magic-byte signature matches** the extension; reject double extensions, null bytes, path separators, control/bidi characters in names. Per-field/per-submission caps are enforced by the Submissions module in Phase 3. `IFileInspector` is a no-op port for future antivirus integration. Do not build a scanner or antivirus platform in v1.
- **Upload ≠ Submit.**
  - *(a) Generic upload (Phase 1C onward):* the upload endpoint creates a generic `Document` with a secure server identity and generic ownership/upload metadata. It knows nothing about submissions or fields.
  - *(b) Submission association (Phase 3):* the Submissions module creates its own association `SubmissionId + FieldKey + DocumentId` when the student attaches an already-uploaded Document to a field of their draft submission. This is a separate step and a separate entity from the upload.
  - On save/final submit the server verifies every referenced Document and its submission-owned association: ownership/context, Document is Active/usable, the field accepts it, and count/type/size rules hold. It finalizes the business linkage in the same transaction as the submission finalization where required. Final submit never uploads the physical file again.
  - Uploading never submits a form. Unauthorized downloads are denied and audited per SR3. "Attach-on-submit", where used, means only this verification/finalization step.
- Download only through `DocumentsController` → handler (ownership/scope/permission check + SR3 audit). Headers: `Content-Disposition: attachment` (inline only for pdf/png/jpeg preview), `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`. Never build paths from request data. Never serve directly.
- Retention and cleanup per ADR-0010. `DocumentCleanupHostedService` removes ONLY explicitly cleanup-eligible incomplete or abandoned upload artifacts, as defined by ADR-0010 together with its age thresholds. Being non-Active is NOT cleanup eligibility: Quarantined, Rejected, retention-related or any future state is never a cleanup candidate merely because it is not Active. Eligibility is an explicit rule, never an inference from status. This does not mean a non-Active state (e.g., Rejected) can never be cleaned up. ADR-0010 may explicitly make a particular state cleanup-eligible after a defined age threshold. Cleanup eligibility always comes from an explicit ADR-0010 rule, never merely from being non-Active. Phase 3 adds one more eligibility rule: a Document with no submission-owned association beyond the configured orphan threshold (currently >24 h, subject to ADR-0010). The cleanup job must not ship before ADR-0010 is Accepted.

## 16. Validation and Persian normalization

Two classes of data. Do not blindly normalize everything.

- **Canonical/normalized data**: identifiers (student number, National ID, mobile), numbers, dates, search keys, comparison keys, option codes. Normalize at the boundary: Persian/Arabic-Indic digits → ASCII; Arabic `ي→ی`, `ك→ک` for comparison keys; trim; collapse whitespace; for identifiers strip everything but expected characters.
- **Original display data**: free-text answers (`Text`, `Textarea`), student names as received from the university, filenames. Preserve exactly what the user/source entered, except: trim ends, normalize line endings, Unicode NFC, strip control characters and bidi override/isolate characters (U+202A–202E, U+2066–2069). Keep ZWNJ (U+200C). Do NOT replace Arabic/Persian letters or digits inside display text.
- Searchable fields keep BOTH: the original (display) and a normalized column or projection (e.g., `Student.NormalizedFullName`; normalized `ValueText` in `SubmissionFieldValue`). Query input is normalized the same way.
- `PersianTextNormalizer` (Domain/Common) is the single implementation. Do not re-implement normalization elsewhere.
- `NationalId` value object: 10 digits, not all-same digits, checksum. Stored ASCII.
- Dates: UI accepts/displays Jalali; storage is `date` or UTC `datetime2` (Gregorian). Conversion only through `JalaliDate` (`System.Globalization.PersianCalendar`). Never store Jalali strings canonically.
- Dynamic validation runs on the server against the pinned `FormVersion`. Draft save is lenient (type/size sanity, no required checks). Final submit is strict. Client checks are UX only.

## 17. EF Core and migrations

- Fluent API in `Infrastructure/Persistence/Configurations`. No persistence data annotations in Domain.
- Explicit max length on every string, explicit decimal precision, dates as `date`/`datetime2`. Statuses that appear in reports are stored as strings.
- Queries: `AsNoTracking`, DTO projection, `Include` only when loading aggregates for change. No lazy loading, no N+1, always paginate lists, never `ToList()` then filter in memory.
- Raw SQL only in Infrastructure, only parameterized (`FromSql`/`ExecuteSql` interpolation).
- `RowVersion` on `FormSubmission`, draft `FormVersion`, draft `WorkflowDefinitionRevision`, `WorkflowInstance`, `WorkflowStepInstance`.
- Required indexes/constraints (declare in configurations): `Student.StudentNumber` unique; `FormVersion(FormDefinitionId, VersionNumber)` unique; one Draft per `FormDefinition` (filtered unique); `WorkflowDefinitionRevision(WorkflowDefinitionId, RevisionNumber)` unique; one Draft revision per definition (filtered unique); `FormField(FormVersionId, Key)` unique; `SubmissionRevision(SubmissionId, RevisionNumber)` unique and `SubmissionRevision(SubmissionId, OperationId)` unique; `WorkflowEvent(WorkflowInstanceId, OperationId)` unique; `SubmissionFieldValue(FieldKey, …)`; `AuditLog(OccurredAtUtc)`; `AuditLog(EntityType, EntityId)`.
- No generic soft-delete. Use status columns. Published forms/revisions, submissions, revisions, workflow events and audit are never deleted.
- **Migrations** live only in `Infrastructure/Persistence/Migrations`. One migration per logical change, descriptive name, reviewed (read Up/Down). Never edit an applied migration. Never `EnsureCreated`. Never auto-migrate in production (development/test only, behind a config flag). Command:
  `dotnet ef migrations add <Name> -p src/GraduateRecords.Infrastructure -s src/GraduateRecords.Web -o Persistence/Migrations`
- Seeds (roles, permissions, reference data) are idempotent.

## 18. Security rules (checklist for every change)

- CSRF: antiforgery on every POST. AJAX sends the `RequestVerificationToken` header via `wwwroot/js/core/http.js`.
- XSS (server): Razor encoding only. No `Html.Raw` on non-constant data. No inline `<script>`/event-handler attributes. Strict CSP (no `unsafe-inline` for scripts). Admin-entered labels/help/options are plain text.
- **XSS (JavaScript): never put dynamic, user-provided or admin-provided text into `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write`, or `Element.setHTML` without an approved sanitizer/HTML policy ADR.** Use `textContent`, `setAttribute` (never for `on*`/`href` with untrusted schemes), `createElement`, `<template>` cloning, and DOM APIs. Form labels, help text, options, student answers, filenames, comments, and API data are ALL untrusted. Validate URLs (`https:`/relative only) before assigning to `href`/`src`.
- IDOR: see §13. Every endpoint taking an id gets an authorization-matrix test.
- Over-posting: ViewModels with explicit properties. Dynamic answers whitelisted by pinned form keys.
- Exports (CSV/Excel): neutralize formula injection (prefix cells beginning with `= + - @ \t \r` with `'`). Row cap. Sensitive fields excluded by default.
- Errors: users see Persian text + correlation id, exactly: `خطایی رخ داده است.` / `کد پیگیری: <correlation-id>`. Never stack traces, SQL, paths or exception messages. The global exception handler logs details.
- Secrets: user-secrets (dev), environment/secret store (prod). Never in `appsettings*.json`, logs, commits. `ValidateOnStart` for options.
- Headers: `X-Content-Type-Options`, `Referrer-Policy`, CSP (incl. `frame-ancestors 'none'`), HSTS in production.
- Data Protection keys persisted durably (location per ADR-0008).
- Rate limiting (built-in) on login and upload endpoints.

## 19. Background jobs, observability, operations

- Exactly two hosted services (`BackgroundService`), no job framework: `UniversitySyncHostedService`, `DocumentCleanupHostedService`.
- Rules: create a new DI scope (`IServiceScopeFactory`) per run, never inject scoped services (`DbContext`) into the singleton; honor `stoppingToken` everywhere (graceful shutdown); `PeriodicTimer` with startup delay and jitter; process in bounded batches; per-item try/catch so one failure never kills the loop; failed items are retried on the NEXT cycle (respecting `LastSyncAttemptAt` backoff), never in a tight loop; **no overlapping execution** (in-process single-flight guard; if more than one app instance is ever deployed, use `sp_getapplock` in Infrastructure); log start/end/counters.
- Phase 0 deliverables: Serilog (structured, no PII), `CorrelationId` middleware (echoed in responses and logs), global exception handler, safe error pages, health checks.
- Health: `/health/live` (process up, anonymous, no detail) and `/health/ready` (database reachable + file root writable; returns status only, never connection strings or paths). The university API is NOT a readiness dependency. Its state may be reported as `Degraded` only. Health endpoints may be anonymous only because deployment health checks need it. Restrict `/health/ready` by network/configuration.
- Hosting, reverse proxy, HTTPS, SQL Server version, backups (database AND file store), key persistence, log retention and deployment strategy are ADRs (0006, 0007, 0008, 0010), each Accepted before the slice that depends on it (§24). Do not assume them.

## 20. Approved packages (everything else needs an ADR)

EF Core + SqlServer provider, ASP.NET Core Identity EF stores, FluentValidation, Microsoft.Extensions.Http.Resilience, Serilog (+ASP.NET Core, console/file sinks), ClosedXML (Phase 5), a PDF library chosen by ADR (Phase 5), xUnit, Testcontainers.MsSql, NetArchTest.Rules, Microsoft.AspNetCore.Mvc.Testing, Shouldly or plain asserts, npm: `tailwindcss` + `@tailwindcss/cli` (4.x, exact version pinned, build-time only). Prefer built-in .NET features. Check licenses before adding anything.

## 21. UI rules (details in `DESIGN.md`)

- **`ui-ux-pro-max` is required for SIGNIFICANT UI work**: new dashboards, the Form Designer, the student form-filling experience, review pages, major layouts/navigation, complex responsive components, workflow visualization, new page-level loading/empty/error design. It is NOT required for trivial changes (adding an input/button, text edits, a column in an existing table, reusing existing components with different data). Use the skill FIRST, before building. Its output is guidance only: adapt it to Razor + Tailwind + `DESIGN.md`. If it conflicts with `DESIGN.md` or this file, `DESIGN.md`/`AGENTS.md` win. It never justifies introducing React or violating architecture. Reuse existing project components before creating new ones. RTL/Persian rules always apply.
- `<html lang="fa" dir="rtl">`. Use Tailwind **logical** utilities (`ms- me- ps- pe- start- end- text-start text-end border-s border-e rounded-s rounded-e`). Never `ml- mr- pl- pr- left- right- text-left text-right float-left float-right` for layout. LTR data (email, URL, student number, National ID, phone) uses `dir="ltr"`.
- **Tailwind v4 is the chosen major version.** CSS-first config in `Assets/css/app.css` (`@import "tailwindcss"; @source …; @theme inline {…}`). No `tailwind.config.js`. Build with `@tailwindcss/cli`. Do not paste v3-era snippets (`bg-opacity-*`, `outline-none` for focus removal, `shadow-sm` meaning "subtle", `@tailwind base`). Use the v4 names listed in DESIGN.md §6.1.
- All user-visible text is Persian. No placeholder English or lorem ipsum. Shared strings in `.resx`, page-specific text inline.
- Components: TagHelper (primitive with variants), Razor partial (static composite), ViewComponent (needs data/logic), vanilla JS module (behavior). Reuse first. Extract after the second real use.
- WCAG 2.1 AA. Student screens mobile-first (≥ 44 px targets). Staff/Admin desktop-first and responsive.
- The three-pane Form Designer layout is a UI recommendation, not an architectural requirement. Keyboard-accessible click/move controls must always remain available.

## 22. Testing expectations

Use the cheapest test that proves the behavior. Do not force SQL Server where a pure unit test suffices. Never use EF InMemory or SQLite to stand in for SQL Server.

| Layer | Test type | Must cover |
|---|---|---|
| Domain | Pure unit | `NationalId`, normalization (canonical vs display), Jalali conversion, form/workflow revision immutability and publish rules, workflow transition table, AutoFill mapper and mode rules, staleness ladder, ContentHash determinism (incl. null workflow binding) |
| Application (pure logic) | Unit with fakes for ports | validators, `SubmissionDataSerializer`, dynamic validator, assignment eligibility, scope extensions |
| Application + EF | SQL Server (Testcontainers/LocalDB), only where persistence/query/transaction/constraint/JSON/concurrency behavior matters | submit/resubmit transactions, revisions and projection, idempotent replay, concurrency, unique indexes, snapshot isolation, backfill, Document schema has no Submission/FormField linkage columns |
| Web | `WebApplicationFactory` | authentication, antiforgery, authorization matrix (anonymous / student-owner / student-other / staff in-scope / staff out-of-scope / staff without permission / admin) for each endpoint taking an id, upload/download, error pages without leakage |
| External API | contract + resilience with fake `HttpMessageHandler` | timeout, 5xx retry, retry cap, 4xx no retry, circuit open, invalid payload does NOT overwrite valid data, identity conflict, error category mapping, "known student signs in while API is down" |
| Architecture | NetArchTest + grep tests | dependency rules (§2), Documents types/modules do not depend on Submissions or Forms types/namespaces, controllers do not use `AppDbContext`, no `Html.Raw` on dynamic data, no `innerHTML/outerHTML/insertAdjacentHTML` in `wwwroot/js` (except approved sanitizer), no `ml-/mr-/pl-/pr-/left-/right-` classes in views/TagHelpers, no `tailwind.config.js` |
| Security | targeted | IDOR, over-posting, upload attacks (bad signature, double extension, traversal, oversize), XSS payload round trip, export formula injection |

Bug fix = failing regression test first. Never delete or weaken tests (including architecture tests) to get green.

## 23. Definition of Done (every change)

1. Builds with zero warnings (`TreatWarningsAsErrors`); all tests pass.
2. §2 dependency rules respected (architecture tests green).
3. Permission + ownership + staff-scope checks implemented and tested per new endpoint.
4. Validation in Application with Persian messages.
5. Audit entries for state changes and classified sensitive reads (§14).
6. Idempotency (§11) implemented for any new state-changing workflow/submission command.
7. Migration (if any) reviewed, correctly named and located; seed idempotent.
8. UI (if any): `ui-ux-pro-max` used when the change is significant; `DESIGN.md` followed; RTL verified; mobile checked for Student screens; loading/empty/error states; keyboard operable; no `innerHTML` with dynamic text.
9. No secrets, PII, or stack traces in code/logs/responses.
10. No unrelated refactors or formatting churn.
11. Docs/ADRs updated if a rule or concept changed.
12. Completion report written (see `OPENCODE-MASTER-INSTRUCTIONS.md` §12).

## 24. ADR schedule (do not guess; ask)

Rule: **an ADR must be Accepted before the first phase/slice that depends on it begins.** A Proposed ADR is not binding. Do not block earlier slices with later decisions.

| Before | ADRs |
|---|---|
| Phase 1A | 0001 authentication model · 0002 provisioning/activation · 0004 staff data scope · 0006 hosting/deployment/log retention · 0007 SQL Server version/backup · 0008 Data Protection key storage |
| Phase 1B | 0003 university API contract |
| Phase 1C | 0010 file retention |
| Phase 1D | 0011 Tailwind v4 browser support · 0012 reference-data source of truth |
| Phase 2 | 0009 Jalali date picker |
| Phase 3 | 0005 Expired/staleness behavior |
| Phase 4 | 0014 workflow claiming |
| Phase 6 | 0013 ProfessorSelector |

If a task depends on a not-yet-Accepted ADR, build only what is safe under every plausible outcome and ask the project owner.
