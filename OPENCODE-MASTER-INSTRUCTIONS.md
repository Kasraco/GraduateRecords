# Instructions

You are implementing the **Graduate Student Electronic Records System**. These instructions govern HOW you work. WHAT the code must be is defined in `AGENTS.md`. Do not restate or reinterpret those rules.

## 1. Read first (every session, before any change)
1. `AGENTS.md` (mandatory rules)
2. `ARCHITECTURE.md` (concepts, flows, boundaries)
3. `DESIGN.md` (only if the task touches UI)
4. `docs/decisions/` (Accepted ADRs only are binding; the ADR schedule is AGENTS.md §24)
5. `ROADMAP.md`: identify the phase and slice (e.g., Phase 1B). Ask the user if unclear.

If a task contradicts these documents, STOP and ask. Do not edit the documents to fit your solution without approval. If the documents contradict each other, report it.

## 2. Scope discipline
- **Do not build the entire system in one prompt.** Implement one vertical slice (one use case end-to-end) at a time, phase by phase, in roadmap order. Do not jump ahead of the current phase.
- If a request spans several use cases, propose a slice order and implement only the first.
- No unrelated changes: no drive-by refactors, renames, formatting sweeps, package upgrades, or file moves. Report out-of-scope problems at the end; do not fix them.
- No speculative features, extension points or abstractions "for later".
- Do not start a slice whose required ADRs (AGENTS.md §24) are not Accepted. Later ADRs never block earlier slices. Forms need no workflow: do not make Phase 2/3 work depend on Phase 4.
- If a slice depends on a not-yet-Accepted ADR, build only what is safe under every plausible outcome and ask the owner.
- The Documents module stays generic in every phase: never add `SubmissionId`, `FieldKey` or `FormFieldId` to `Document` or the upload endpoint, and never make Phase 1C file code reference Submission/FormField types. The business association is a Submissions-module entity added in Phase 3. Cleanup eligibility is an explicit ADR-0010 rule, never merely "not Active". This does not mean a non-Active state (e.g., Rejected) can never be cleaned up: ADR-0010 may explicitly make a state eligible after a defined age threshold.

## 3. Inspect before you plan
- List the solution tree. Open the files in the area you will touch. Find the closest existing feature and copy its pattern.
- Search for existing components/helpers first (`UI/TagHelpers`, `UI/ViewComponents`, `Views/Shared/Ui`, `Common/`).
- Check `git status`. Never overwrite uncommitted user work.

## 4. Plan, then implement
Write a plan first (≤ 15 lines): slice goal; files per project; entities/migration; permission(s), ownership and **staff-scope** checks; audit actions (and whether any is a classified sensitive read); idempotency need (`OperationId` issued at render time, before the command); tests; UI components to reuse/create; ADR dependencies. Wait for approval if the plan adds a package, project, abstraction, or deviates from `AGENTS.md`.
Slice order: Domain (invariants + unit tests) → Application (command/query, handler, validator, ports) → Infrastructure (EF configuration, migration, adapters) → Web (ViewModel, controller, view, JS) → tests → report.

## 5. Migrations
- Create a migration only when the model changed, using the command in `AGENTS.md §17`, with a descriptive name (`AddWorkflowDefinitionRevision`).
- Read the generated migration. Check indexes, max lengths, constraints, filtered unique indexes and unintended drops. Never edit an applied migration; add a new one.
- Never apply migrations to non-development databases. Seeds are idempotent.

## 6. UI work
1. Decide whether the work is **significant** (AGENTS.md §21: dashboards, Form Designer, student form experience, review pages, major layouts, complex responsive components, workflow visualization). If yes, **first use the installed `ui-ux-pro-max` skill**. If the change is trivial (an input, a button, copy, an extra table column), do not run it. Use existing components.
2. Treat the skill's output as guidance: adapt to Razor + Tailwind **v4** + `DESIGN.md`. Never introduce React or a client framework. `DESIGN.md` wins on conflict. RTL/Persian always applies.
3. Implement with TagHelpers/partials/ViewComponents and vanilla JS (`DESIGN.md §14`). Never use `innerHTML`/`outerHTML`/`insertAdjacentHTML` with dynamic text.
4. **Validate** before declaring done: `npm run build:css`, run the app, open the page; check RTL alignment, Persian wrapping, 360 px (Student) and 1280 px (Staff/Admin), keyboard-only navigation, focus visibility, loading/empty/error/403/404 states, validation display, double-submit protection. If you cannot run a browser, say so and list what is unverified. Never claim visual verification you did not perform.
5. Do not change the Tailwind major version or add `tailwind.config.js`.

## 7. Security
Apply `AGENTS.md §13–§16, §18` to every change. For each new endpoint answer in your plan: who may call it (permission); which ownership AND staff-scope checks apply; what is validated; what is audited; what could be tampered with (ids, status, version, owner, step, OperationId); which untrusted text reaches the DOM.
Never trust request data for identity, roles, ownership, scope, status, form version or workflow step. Never log or display secrets, tokens, full National IDs, full `DataJson`, stack traces, raw API payloads. Do not add `[AllowAnonymous]` (beyond login/error/health/static infrastructure), disable antiforgery, weaken cookie/password/lockout settings, add CDNs, or use `Html.Raw`/`innerHTML` on dynamic data.

## 8. Authorization
Permission attribute on the action **and** ownership + staff-scope check in the handler (use `ScopeExtensions`). Add a case to the authorization matrix (anonymous / student-owner / student-other / staff in-scope / staff out-of-scope / staff without permission / admin). New permissions: add the constant, seed it, document it in `ARCHITECTURE.md §10`.

## 9. Audit
Declare audit actions for every state change and classified sensitive read (AGENTS.md §14 SR1–SR5). Business audit goes in the same transaction. Classified sensitive reads fail closed. Other reads and operational events do not. Never put sensitive payloads in audit. Store `ActorUserId` and the `ActorDisplayName` snapshot; never query Identity to rebuild historical names.

## 10. Tests
Write tests with the code. Use the cheapest test that proves the behavior (AGENTS.md §22): Domain units for invariants/transitions; pure Application unit tests; SQL Server tests only for persistence/transaction/constraint/concurrency/JSON/schema behavior (never EF InMemory/SQLite as a stand-in); `WebApplicationFactory` for endpoints/authorization; fake `HttpMessageHandler` for the university client. Run the full suite before reporting. A bug fix starts with a failing regression test. Never delete or weaken tests, including architecture tests.
Mandatory named tests when the area is touched: published form/workflow revision immutability; editing a definition mid-flight does not change in-flight instances; invalid university payload does not overwrite valid student data; known student signs in with API down; idempotent replay of submit/decision; staff-scope filtering with a restrictive fake scope; no `innerHTML` in JS; Documents do not depend on Submissions/Forms (architecture test) and the Document schema has no Submission/FormField linkage columns (SQL Server test).

## 11. When blocked or uncertain
Ask a specific question with 2–3 options and your recommendation. Do not guess any decision owned by an ADR in the AGENTS.md §24 schedule. Ask only about decisions that gate the current slice. Record resolutions as ADRs.

## 12. Completion report (required at the end of every slice)
```text
Slice: <name> (Phase N / slice)
Summary: <2–4 sentences>
Files changed: <grouped by project>
Migration: <name or none>, reviewed: yes/no
Security: permissions; ownership + staff-scope checks; validation; over-posting/IDOR; untrusted-text handling
Audit: actions emitted; any classified sensitive read (SRx)
Idempotency/concurrency: OperationId + RowVersion handling, or N/A
Tests: added/updated; full-run result (counts); gaps
UI: ui-ux-pro-max used (yes/no/N/A and why), components reused/created, RTL/mobile/keyboard/state checks performed vs not performed
ADR dependencies: used/pending
Deviations from AGENTS/ARCHITECTURE/DESIGN: none | list + why + ADR
Out-of-scope issues noticed (not changed): <list>
Suggested next slice: <name>
```
