# ROADMAP.md: Implementation Roadmap

Rules: one phase at a time; one vertical slice at a time inside a phase; each phase ends with its completion criteria met. Slices are implementation units, not separate roadmap phases.
**ADR rule:** an ADR must be Accepted before the first phase/slice that depends on it begins. A Proposed ADR is not binding. Later ADRs never block earlier slices.

**ADR schedule:** Phase 1A → 0015, 0002, 0004, 0006, 0007, 0008 (0015 supersedes 0001) · Phase 1B → 0003 · Phase 1C → 0010 · Phase 1D → 0011, 0012 · Phase 2 → 0009 · Phase 3 → 0005 · Phase 4 → 0014 · Phase 6 → 0013.

## Phase 0: Decisions, bootstrap, observability (≈ 1–2 weeks, mostly non-code)
**Goal:** establish a safe, observable skeleton and the decisions required to begin Phase 1A.
**ADRs:** Phase 0 requires only the ADRs needed to begin Phase 1A to be **Accepted**: 0015 (supersedes 0001), 0002, 0004, 0006, 0007, 0008. Later ADRs are Accepted before their dependent slice (schedule above).
**Build:** repo, solution, `Directory.*.props`, four projects + test projects, CI (build, test, Tailwind build), architecture tests (dependency rules, `innerHTML` grep, logical-class grep), Serilog, `CorrelationId` middleware, global exception handler with the standard Persian error page, security headers, health checks (`/health/live`, `/health/ready`), Tailwind v4 pipeline, Vazirmatn, empty RTL layout.
**Done when:** CI builds green; architecture tests pass; an RTL Persian page renders; an unhandled exception shows `خطایی رخ داده است.` + `کد پیگیری` with no leakage; health endpoints respond without sensitive detail; ADRs 0015 (superseding 0001), 0002, 0004, 0006, 0007 and 0008 are Accepted (not merely Proposed).

## Phase 1: Foundation
**Goal:** secure foundation without business forms. Slices:

### 1A: Foundation + Identity + permissions
**Requires Accepted:** ADR-0015 (dual authentication; supersedes ADR-0001), 0002, 0004, 0006, 0007, 0008.
Features: sign-in/out (University SSO primary + local National ID/password for eligible Staff/Administrator accounts per ADR-0015; Students SSO-only; `/dev/login` removed), lockout, password policy, cookie settings, rate limiting, roles/permissions model, `PermissionPolicyProvider`/`HasPermission`, `ICurrentUser` with `StaffScope` (+ `ScopeExtensions`, default `All` until ADR-0004 policy is applied), `IIdentityAdministration` + admin UI for users/roles/permissions, seeded Student/Staff/Administrator roles, last-admin protection.
Security: no default passwords; permission-escalation check; SecurityStamp revalidation.
Tests: authorization matrix scaffold, escalation, last-admin, scope extension test with fake scope, lockout.

### 1B: Student + university API + synchronization
**Requires Accepted:** ADR-0003.
Features: `Student` entity (+ `NationalId`, normalizer, Jalali helpers), `IStudentDirectory` + resilient client, on-demand bounded sync, `UniversitySyncHostedService` (scoped DbContext, cancellation, no overlap), staleness ladder (non-overlapping ranges) + banner, student search, sync metadata with error **categories**, integrity rule (validation, identity conflict, no overwrite on bad data).
Security: payload validation, National ID masking, no PII in logs, secret credentials.
Tests: NationalId/normalization/Jalali units; **known student signs in while API down**; timeout/5xx retry/retry cap/4xx no retry/circuit; **invalid or incomplete payload does not overwrite valid data**; identity conflict; error-category mapping; staleness boundaries.

### 1C: Audit + secure file infrastructure
**Requires Accepted:** ADR-0010.
Features: `AuditLog` (with `ActorUserId` + `ActorDisplayName` snapshot) + `IAuditWriter` (transactional + fail-closed for SR1–SR5 only), audit viewer (`audit.view`, SR5), `IFileStorage`, `IFileInspector` (no-op), generic `Document` metadata, upload/download pipeline with a test harness page, `DocumentCleanupHostedService`. Generic only: Document and upload carry no Submission/FormField dependency, now or later. Cleanup removes only explicitly cleanup-eligible incomplete/abandoned artifacts per ADR-0010. Non-Active status alone is never eligibility. This does not mean a non-Active state (e.g., Rejected) can never be cleaned up: ADR-0010 may explicitly make a particular state cleanup-eligible after a defined age threshold. Eligibility always comes from an explicit ADR-0010 rule, never merely from being non-Active. The cleanup job does not ship before ADR-0010 is Accepted.
Security: GUID names, outside `wwwroot`, magic bytes, double-extension, traversal, authorized download, headers.
Tests: audit append-only; fail-closed vs best-effort behavior; upload attack matrix; download authorization/IDOR; cleanup leaves Quarantined/Rejected/other non-Active Documents untouched unless an explicit ADR-0010 rule makes that state eligible and its threshold has passed.

### 1D: Reference data + UI primitives
**Requires Accepted:** ADR-0011, 0012.
Features: Faculty/Department/Program read model per ADR-0012 (sync or manual CRUD; no Professor management); UI primitives (button, badge, alert, card, field controls, page header, empty state, status badge, pagination, dialog, toast, icon), student and staff layouts, dev-only UiKit page.
Tests: reference-data sync integrity (if synced) or CRUD authorization (if manual); UI checks per DESIGN.md §18.

**Phase 1 done when:** a known student signs in with the university API down; staff can search students within scope; files round-trip securely; privileged actions are audited; reference data works per ADR-0012; all tests above pass.

## Phase 2: Form definitions, designer, assignment, AutoFill
**Requires Accepted:** ADR-0009.
**Goal:** admins can design, version, publish and assign forms. Nothing in Phase 2 depends on Phase 4.
Features: `FormDefinition/Version/Field` and Designer (DESIGN.md §15; v1 scope only), 9 field types (no ProfessorSelector), publish with `ContentHash` and immutability (domain + interceptor), retirement semantics, `FormAssignment`/`AssignmentRule` (OR), AutoFill (`AutoFillSource` + `AutoFillMode`), `IsReportable`/`IsSensitive` (mutually exclusive), sensitive-snapshot permission, `DynamicFormRenderer` contract established and **Preview** mode implemented and validated (Edit arrives in Phase 3, ReadOnly in Phase 4). Forms publish without any workflow. No workflow binding exists in Phase 2. Canonical hash content already includes a null workflow binding.
Entities: `FormDefinition, FormVersion, FormField, FormAssignment, AssignmentRule`.
Security: `forms.*` permissions; plain-text labels and options (no HTML); key validation; `RowVersion` on draft saves; audit of publish/assign.
Tests: version lifecycle/immutability (domain + interceptor + SQL), key stability on clone, ContentHash determinism (null binding), AutoFill mode/type matrix, National ID snapshot guard, assignment eligibility, designer save validation, authorization.
**Done when:** an admin creates → previews → publishes v1 → creates v2 draft → publishes v2, and v1's published definition is unchanged (its `ContentHash` still matches the canonical content).

## Phase 3: Submissions
**Requires Accepted:** ADR-0005.
**Goal:** students complete forms safely. Phase 3 does not need Phase 4: forms without a workflow end in `Submitted`.
Features: available-forms list, `StartSubmission` (pins version; applies InitialValue pre-fill), lenient draft save + autosave (updates `DataJson` only, never creates a revision), `DynamicFormRenderer` **Edit** mode, binding by pinned keys, strict server validation (Persian digits, Jalali, canonical-vs-display normalization), Snapshot AutoFill recomputed server-side, attachments (generic upload is immediate and independent of submit; the Submissions module adds its own association `SubmissionId + FieldKey + DocumentId`; save/final submit verifies every referenced Document and its association and finalizes the linkage transactionally; caps; the generic Document entity and upload endpoint are unchanged), review-and-submit step, **idempotent** final submit (`OperationId` issued at render time) creating `SubmissionRevision #n` + `SubmissionFieldValue` in one transaction, submission history. Final submit ends in `Submitted` for forms with no workflow (all forms until Phase 4). Phase 4 adds `StartWorkflow` for forms whose `WorkflowDefinitionId` is set. Extend `DocumentCleanupHostedService` with one explicit eligibility rule: Documents with no submission-owned association beyond the orphan threshold (>24 h, per ADR-0010). Non-Active status never implies eligibility on its own.
Entities: `FormSubmission, SubmissionRevision, SubmissionFieldValue`, submission-owned Document association (naming is an implementation detail).
Security: ownership (404 for others), staff-scope-ready queries, over-posting protection, `OperationId` replay, `RowVersion`, file ownership verification.
Tests: validation per field type; unknown keys discarded; autosave creates no revision; submit creates exactly one revision under double-click (replay creates no duplicate revision/audit); pinned version survives new publish and retirement; projection follows the submitted version's reportability; IDOR matrix; Persian fixtures; a Submitted no-workflow submission rejects further edits with Conflict; **Architecture test:** Documents types/modules must not depend on Submissions or Forms types/namespaces. **SQL Server integration test:** the Document persistence schema/table contains no `SubmissionId`, `FieldKey`, `FormFieldId`, or any other Submission/FormField business-linkage columns. Documents in a non-Active state are removed only when an explicit ADR-0010 rule makes that state cleanup-eligible and its age threshold has passed. Otherwise they are left untouched; unreferenced Documents past the orphan threshold are removed only under the explicit Phase 3 rule.
**Done when:** a student saves drafts on mobile, uploads documents, and final-submits with a clear Draft/Final distinction, and cannot read or modify another student's data.

## Phase 4: Workflow
**Requires Accepted:** ADR-0014.
**Goal:** staff process submissions of workflow-enabled forms with immutable in-flight workflows.
Features: `WorkflowDefinition` + `WorkflowDefinitionRevision` admin (Draft → Published → Retired; one step in v1; step permission), the nullable `FormVersion.WorkflowDefinitionId` column and the optional binding control in the designer (Draft versions only; publishing never requires a Published revision; final submit of a bound form requires one), `StartWorkflow` (revision id + full runtime snapshot), review queue (scope-filtered, workflow-enabled submissions only), `Review`/`Release` (minimal claim per ADR-0014), approve/reject/return (comment rules, actor ≠ owner, snapshotted step permission), resubmit (revision #n+1, Round+1), `WorkflowEvent` timeline (with `ActorDisplayName` snapshot), review page (DynamicFormRenderer **ReadOnly** mode + decision panel), idempotent actions via `OperationId`.
Entities: `WorkflowDefinition, WorkflowDefinitionRevision, WorkflowStepDefinition, WorkflowInstance, WorkflowStepInstance, WorkflowEvent`.
Security: step permission from the snapshot; scope on every by-id handler; SR4 audit for staff opening submissions; audit of every decision; `RowVersion`.
Tests: transition table; **editing/publishing a new revision mid-flight does not change in-flight instances**; revision immutability; Retired revision starts no new instances but in-flight continues; double-click idempotency; concurrent decisions; return/resubmit cycle; claim/release; authorization matrix incl. scope; a form without workflow still submits to `Submitted`; a bound form with no Published revision fails final submit with `Unavailable`; binding change on a Published version is rejected; changing it via a new Draft version yields a new `ContentHash`; Phase 2 version hashes still verify after the Phase 4 migration.
**Done when:** submit → review → return → resubmit → approve works end-to-end and the snapshot-isolation and idempotency tests pass.

## Phase 5: Search, reporting, dashboards, exports
Features: student/submission search, staff dashboard counters (scoped), report builder over relational columns + `SubmissionFieldValue`, ad-hoc `JSON_VALUE/OPENJSON` queries in `Infrastructure/Reporting`, explicit `RebuildFieldProjection` and `BackfillFieldProjection` commands (`reports.backfill`, audited, bounded), Excel export (cap, formula neutralization, sensitive excluded, SR5), PDF export (library by ADR after a Persian/RTL spike), measured index tuning. No-workflow submissions never enter the review queue. Staff reach them through search, student profile and reports under existing rules (`submissions.view-any` + staff scope; SR4 audit when opening).
Security: `reports.*` permissions, scope on all report queries, sensitive-field exclusion, export audit.
Tests: report correctness on fixtures; projection rebuild equals live projection; backfill is explicit and idempotent and does not run automatically; formula-injection neutralization; PDF Persian rendering sample; performance smoke tests on representative volumes; no-workflow submissions visible via search/reports within scope only.
**Done when:** core reports meet agreed latency on realistic data and exports are audited.

## Phase 6: Extensions (each a separate approved mini-project: ADR + tests + rollout notes)
**Requires Accepted:** ADR-0013 (for ProfessorSelector).
AND/NOT assignment rules · multi-step sequential/advanced workflows (lift `MaxStepsV1`) · notifications via a port · additional integrations · university SSO (if not chosen earlier) · antivirus via `IFileInspector` · admin 2FA (recommended before go-live for admins if not earlier) · `CheckboxGroup` · `ProfessorSelector` + professor sync (ARCHITECTURE.md §5.8) · lifecycle behavior for no-workflow submissions (cancel/close) · other approved extensions.

## Cross-phase invariants
Four production projects and one-way dependencies · published form versions and workflow revisions immutable · `ContentHash` covers the workflow binding (null until Phase 4) · submissions pinned to a form version · a form may be published and used without a workflow (`Draft → Submitted`) · workflow = revision id + runtime snapshot · `DataJson` current / `SubmissionRevision` immutable / `SubmissionFieldValue` derived · permission + ownership + staff scope in every handler · idempotent state-changing commands (`OperationId` before the command) · auth separate from sync · generic Documents outside `wwwroot` (never Submission-aware) · append-only audit with actor snapshots · Tailwind v4 · no React, repositories, MediatR, BPMN or event sourcing.
