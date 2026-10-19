# ADR-0007: SQL Server version, edition and database configuration

- **Status:** Accepted
- **Date:** 2026-10-18
- **Deciders:** Project Owner
- **Related documents/ADRs:** ADR-0006 (hosting and deployment), ADR-0002 (OTP and provisioning), future ADR on dynamic forms/submissions (`DataJson`), future ADR on reporting

## Context
The project owner states that university IT normally installs the latest SQL Server, currently SQL Server 2025, in the Enterprise edition, on a new installation. SQL Server Agent and Full-Text Search are available. The instance server collation is set to Persian/Arabic at install time. The university has no at-rest encryption requirement. Development uses a self-signed server certificate; production uses a valid certificate. The developer can install a matching Developer edition locally.

The application needs: Persian text handling, JSON querying for dynamic form data, reliable scheduled maintenance, and the same behavior in development and production.
- **Date:** 2026-10-18
- **Deciders:** Project Owner
- **Related documents/ADRs:** ADR-0006 (hosting and deployment), ADR-0002 (OTP and
  provisioning), future ADR on dynamic forms/submissions (`DataJson`), future ADR on
  reporting

## Context
The project owner states that university IT normally installs the latest SQL Server,
currently SQL Server 2025, in the Enterprise edition, on a new installation.
SQL Server Agent and Full-Text Search are available. The instance server collation is
set to Persian/Arabic at install time. The university has no at-rest encryption
requirement. Development uses a self-signed server certificate; production uses a
valid certificate. The developer can install a matching Developer edition locally.

The application needs: Persian text handling, JSON querying for dynamic form data,
reliable scheduled maintenance, and the same behavior in development and production.

## Decision

### 1. Version and edition
1.1 Production: SQL Server 2025, Enterprise edition, with the latest cumulative update
    at installation. IT keeps the instance patched.
1.2 Development: SQL Server 2025 Developer edition (same version, same engine
    features as Enterprise), same server collation and same database compatibility
    level as production.
1.3 The database compatibility level is the level of the installed version (170 for
    SQL Server 2025). It is verified in the deployment checklist and the same value is
    used in Development and Production.
1.4 No lock-in to 2025-only features. The application must not depend on features
    available only in SQL Server 2025. Features available since SQL Server 2016/2022
    (for example `JSON_VALUE`, `OPENJSON`, computed columns, filtered indexes) are
    allowed. Rationale: SQL Server 2025 is new and less field-proven; staying within
    older features keeps a fallback to SQL Server 2022 possible.
1.5 Enterprise-only features (for example online index rebuilds, partitioning) may be
    used only where a Standard-edition alternative exists or the benefit is clearly
    documented, so a licence change would not break the application.
1.6 Tests that touch the database run against a real SQL Server instance of the same
    version and collation. EF Core InMemory and SQLite are not allowed for such tests,
    because collation, `JSON_VALUE`, computed columns and indexes behave differently.

### 2. Collation and text
2.1 Database collation: `Persian_100_CI_AS`, equal to the instance server collation.
    IT must choose this collation at SQL Server setup, because changing the server
    collation later requires a rebuild.
2.2 The database collation must equal the instance (tempdb) collation. A mismatch
    causes "cannot resolve collation conflict" errors with temporary tables and table
    variables. The deployment checklist verifies both values. Using a different
    database collation than the instance's is not allowed.
2.3 All text columns use Unicode types (`nvarchar`). UTF-8 collations are not used.
2.4 The collation does not treat Arabic and Persian letter forms (ي/ی, ك/ک) or Persian
    and Arabic-Indic digits as equal. The application always normalizes text and digits
    before saving, searching and comparing (requirement from the project brief).
    Unique constraints and lookups (National ID, student number) therefore store
    normalized values only.
2.5 Dates and times: canonical dates are stored as `date`, instants as `datetime2`
    in UTC (or `datetimeoffset`). Jalali strings are never stored as canonical values.

### 3. Database settings
3.1 Recovery model: Full (point-in-time recovery, required by ADR-0006 backup plan).
3.2 `READ_COMMITTED_SNAPSHOT` is ON to reduce reader/writer blocking in the web
    workload.
3.3 Query Store is ON, because there are no monitoring tools and it is the main way
    to diagnose slow queries.
3.4 `PAGE_VERIFY CHECKSUM`; `AUTO_CLOSE` OFF; `AUTO_SHRINK` OFF; `TRUSTWORTHY` OFF.
3.5 Schemas: business tables in the default schema; application logs in schema `log`
    and audit records in schema `audit` (ADR-0006 and ADR-0004).
3.6 Accounts (ADR-0006 4.4): a runtime account with data read/write permissions only
    (and insert on `log`), and a separate migration account used manually at
    deployment.

### 4. SQL Server Agent
4.1 Agent runs database-level maintenance only: index and statistics maintenance, log
    purge (`log` schema, default retention 90 days, configurable), and consistency
    checks. Backups are performed by the infrastructure unit (ADR-0006).
4.2 Business jobs (Sama synchronization, OTP challenge cleanup) run inside the
    application, not in SQL Agent, because they call external HTTP services and use
    application code. The IIS application pool must be configured so that these jobs
    are not stopped by idle timeout (see ADR-0006/ADR-0003).
4.3 Agent jobs are scripted and stored in the repository (`/database/jobs`) so they
    can be recreated. Job failures are visible in the Agent history that IT reviews.

### 5. JSON and reporting
5.1 `DataJson` is stored as `nvarchar(max)` with an `ISJSON` check constraint in the
    first release. The native `json` data type and JSON indexes of SQL Server 2025
    are not adopted yet.
5.2 Frequently filtered or sorted dynamic fields are exposed through the derived
    `SubmissionFieldValue` projection (AGENTS §8, ARCHITECTURE §14), not through
    per-form or dynamically created computed columns. Relational columns are used for
    fixed system-level fields; rare ad-hoc queries may use `JSON_VALUE`/`OPENJSON`
    in Infrastructure only. Per-form tables, per-form columns and dynamically created
    computed columns are forbidden.
5.3 The native `json` type is re-evaluated in Phase 5 (JSON reporting optimization)
    with a benchmark on realistic data volume, and adopted only if it provides a
    measurable benefit and EF Core support is verified.

### 6. Full-Text Search
6.1 The Full-Text feature is installed at initial setup so no later reinstall is
    needed.
6.2 It is not used in the first release. Student search (name, National ID, student
    number) uses indexed searches on normalized columns.
6.3 Full-text indexing is considered in Phase 5 only if prefix/equality search is
    insufficient. Persian text is handled by normalizing characters and digits first;
    Persian has no dedicated linguistic support (stemming) in SQL Server full-text, so
    behavior must be tested before relying on it. Verify language support at that time.

### 7. Encryption
7.1 Transport: all connections are encrypted. Production connection strings use
    `Encrypt=True` (strict encryption if IT enables it) and `TrustServerCertificate=False`
    with a valid certificate whose name matches the database server name.
7.2 Development may use the self-signed certificate with `TrustServerCertificate=True`.
    That setting is configured only through Development configuration. The application
    refuses to start in Production if the connection string contains
    `TrustServerCertificate=True`.
7.3 At-rest encryption (TDE) is not required by the university and is not part of this
    release. If IT enables it later, the application needs no change. The database
    stores National IDs and contact details in plain columns (needed for search and
    uniqueness), so protection relies on authorization, audit and access control to the
    server and backup storage. IT is asked to restrict access to backup files.
7.4 Always Encrypted and column-level encryption are not used.

### 8. EF Core
8.1 EF Core 10 with the SQL Server provider. The provider's compatibility-level
    setting is aligned with the database compatibility level (verify the exact option
    in the EF Core 10 documentation during implementation).
8.2 Migrations are generated by EF Core and applied at deployment as a reviewed
    idempotent script (ADR-0006), never automatically at startup in Production.

## Alternatives considered
| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| SQL Server 2025 Enterprise (chosen) | Longest support, matches IT standard, current features | New release, less field-proven | Chosen, with patching and "no 2025-only features" rule |
| SQL Server 2022 | Mature, widely deployed | IT standard is the latest; shorter remaining support | Remains the fallback because of rule 1.4 |
| SQL Server 2019 or older | Familiar | Mainstream support has ended or is ending | Security and lifecycle risk |
| SQL Server Standard/Express | Cheaper | Express has size limits and no Agent; Enterprise already licensed | Not needed |
| PostgreSQL or other databases | Free, strong JSON | Team and IT standard are SQL Server; EF and SQL Server JSON plan already fixed | Out of scope |
| Native `json` type now | Efficient storage, newer | Newer feature; computed-column/index and tooling behavior must be proven | Deferred to Phase 5 benchmark |
| `Persian_100_CI_AS` vs Arabic collation | Persian-specific rules | None significant | Persian collation chosen; must equal instance collation |

## Consequences
- IT must set the instance collation correctly at installation. This is an
  irreversible-in-practice decision and must be confirmed before the database is created.
- Developers must install the same SQL Server version and collation locally;
  differences cause bugs that appear only in production.
- Application logs and Query Store increase database size; retention jobs are required.
- Dynamic-form reporting design must choose per-field indexing (computed columns or
  relational promotion) instead of relying on JSON scans.
- A new SQL Server version means early cumulative updates matter; IT patching cadence
  must be agreed.

## Acceptance criteria
- Production and Development report the same SQL Server major version, compatibility
  level and server collation (`Persian_100_CI_AS`).
- The application database collation equals the instance and tempdb collation.
- Integration tests run against a real SQL Server instance with the same collation.
- `DataJson` columns have an `ISJSON` check constraint.
- A production configuration with `TrustServerCertificate=True` fails at startup.
- `READ_COMMITTED_SNAPSHOT` and Query Store are enabled, recovery model is Full.
- Agent jobs for maintenance and log purge exist as scripts in the repository.
- No code path depends on a feature that exists only in SQL Server 2025.
