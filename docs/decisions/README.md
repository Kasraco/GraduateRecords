# Architecture Decision Records (ADRs)

ADRs record decisions that change or complete the architecture. They are the only approved way to deviate from `AGENTS.md`, `ARCHITECTURE.md` or `DESIGN.md`.

## Naming and location
- File: `docs/decisions/ADR-NNNN-short-kebab-title.md` (four-digit sequence, never reused). Template: `ADR-TEMPLATE.md`.
- One decision per file. Do not edit an accepted ADR's decision. Write a new ADR that supersedes it and mark the old one `Superseded by ADR-XXXX`.

## Status values
`Proposed` → `Accepted` | `Rejected` → (later) `Superseded` / `Deprecated`. Only the project owner accepts an ADR. Agents may draft `Proposed` ADRs and must not treat them as binding.
**Rule:** an ADR must be Accepted before the first phase/slice that depends on it begins. A Proposed ADR is not a binding decision.

## When an ADR is required
New package/project/abstraction; any item in `AGENTS.md §1`; any deviation from the documents; any decision listed in the index; any new fail-closed audit category; any new field type or workflow capability.

## Index
| ID | Title | Status | Needed before |
|---|---|---|---|
| ADR-0001 | Authentication model | Accepted  | Phase 1A |
| ADR-0002 | User provisioning and activation | Accepted  | Phase 1A |
| ADR-0003 | University Student API contract | Pending | Phase 1B |
| ADR-0004 | Staff data scope | Accepted  | Phase 1A (hook exists regardless) |
| ADR-0005 | Expired/staleness policy | Pending | Phase 3 |
| ADR-0006 | Hosting, deployment, log retention | Accepted  | Phase 1A |
| ADR-0007 | SQL Server version and backup | Accepted  | Phase 1A |
| ADR-0008 | Data Protection key storage | Accepted  | Phase 1A |
| ADR-0009 | Jalali date picker | Pending | Phase 2 |
| ADR-0010 | File retention, cleanup eligibility rules/thresholds, file-store backup | Pending | Phase 1C |
| ADR-0011 | Tailwind CSS v4 browser-support confirmation | Baseline chosen (v4); confirmation pending | Phase 1D |
| ADR-0012 | Reference-data source of truth | Pending | Phase 1D |
| ADR-0013 | ProfessorSelector requirement | Pending (default: deferred to Phase 6) | Phase 6 |
| ADR-0014 | Workflow claiming | Pending (default: minimal claim) | Phase 4 |

Keep this table current whenever an ADR is added or changes status.
