# ADR-0006: Hosting and deployment

- **Status:** Accepted
- **Date:** 2026-10-18
- **Deciders:** Project Owner
- **Related documents/ADRs:** ADR-0001 (authentication), ADR-0002 (provisioning, OTP),
  ADR-0004 (staff data scope), future ADR-0003 (Sama integration), future ADR on
  file storage

## Context
The system is an internal university application handling sensitive student data.
It depends on systems inside the university network (Sama web services, the SSO at
`idp.ajums.ac.ir`) and one external service (Melipayamak SMS gateway, which requires
an allowed IP).

Facts from the project owner:
- Hosting is on-premises at the university; the server is administered by university
  IT together with the development team; it sits behind the university firewall and
  is subject to vulnerability scans.
- OS and web server: Windows Server + IIS. SQL Server is a new installation.
- The application gets a subdomain of `ajums.ac.ir`. Students use it on campus and
  also from outside the campus.
- Environments: Development and Production. Deployment is manual.
- Backups, disk allocation and storage are provided by the infrastructure unit.
- User count is unknown but expected to be large (peaks at term start).
- Logs go to the database; there is no monitoring tooling. Secrets are provided as
  environment variables. Server time is synchronized by NTP. Planned downtime at
  night is acceptable.

## Decision

### 1. Platform
1.1 Host: on-premises Windows Server in the university data center. Cloud hosting is
    not used.
1.2 Web server: IIS with the ASP.NET Core Hosting Bundle for .NET 10 (LTS),
    in-process hosting model, one dedicated application pool for the application.
1.3 The application is published framework-dependent (`dotnet publish -c Release`).
    The runtime is kept patched. The development team and IT jointly own updating
    the Hosting Bundle after .NET security releases; scan findings about an outdated
    runtime are treated as defects.
1.4 Database: a new SQL Server installation on a supported version (2022 or later
    preferred). Express edition is not acceptable for production (database size limit,
    no SQL Server Agent). The database compatibility level must support the JSON
    functions needed for reporting (ADR on submissions).
1.5 Topology: the application server and the database server should be separate
    machines. Running both on one machine is acceptable for the first release only
    if infrastructure cannot provide two. The SQL Server port is never reachable from
    outside the data center network.
1.6 Single application server in the first release. The application must stay
    stateless where practical, so adding a second node later is possible without a
    redesign (see Consequences). No load balancer, no containers, no orchestration.

### 2. Network and exposure
2.1 The application is reachable by students and staff on campus and from the
    Internet, over HTTPS only, through the university perimeter (firewall or reverse
    proxy). This is required because students sign in through the SSO from outside the
    campus and the browser must be redirected back to the application.
2.2 Only port 443 is exposed publicly (port 80 only to redirect). The database,
    management endpoints, health endpoints and RDP/WinRM are never publicly reachable.
2.3 TLS: certificate for the application subdomain obtained and renewed by university
    IT; TLS 1.2 or later; HTTP to HTTPS redirect; HSTS enabled in Production.
    Certificate expiry is tracked by IT and the development team.
2.4 If a reverse proxy terminates TLS in front of IIS, the application configures
    Forwarded Headers with the proxy's address as the only trusted proxy. This is
    required for correct client IP in audit logs and rate limiting.
2.5 Outbound rules (least privilege): the application server may reach only Sama,
    `idp.ajums.ac.ir`, the SMS gateway host `rest.payamak-panel.com`, the SQL Server,
    NTP, DNS, and any endpoint required for certificate revocation checks.
2.6 SMS gateway: the fixed outbound IP seen by Melipayamak is registered as an allowed
    IP in the provider panel (ADR-0002 rule 6.5). Infrastructure confirms that the
    outbound address is stable.
2.7 The SSO redirect and post-logout URIs for production use the production subdomain
    only (ADR-0001).

### 3. Environments
3.1 Two environments: Development and Production.
3.2 Development runs on developer machines with a local SQL Server instance.
    Production data is never copied to Development.
3.3 Development uses a separate SSO client (own client id and secret, localhost
    redirect URIs). Production uses its own client. A Development secret must never
    work against Production.
3.4 In Development, Sama and the SMS gateway are replaced by stub implementations
    behind the same interfaces unless a Sama test instance is provided. A
    development-only OTP delivery/testing mechanism (for example an in-memory test
    provider or a developer inbox surfaced in the UI) is permitted, but the OTP value
    must never be written to normal application logs or the console logger, matching
    ADR-0002 rule 6.8. Any such mechanism is disabled by environment check outside
    Development.
3.5 The Production environment name is `Production`. Developer exception pages and
    detailed errors are disabled. Users see only generic Persian error pages.

### 4. Configuration and secrets
4.1 Non-secret defaults live in `appsettings.json`. Environment-specific values and
    all secrets are provided as environment variables (for example `Sso__ClientSecret`).
4.2 On IIS, environment variables are set on the application pool (not in
    `web.config`, which sits in the publish folder). Access to this configuration
    is limited to server administrators.
4.3 No secret is committed to source control, written to a log, copied into
    documentation or put into an AI-agent prompt. The Production SSO secret, Sama
    service-account credentials, Melipayamak credentials and database credentials are
    separate values with separate owners.
4.4 Database accounts: the runtime account has only the data permissions needed
    (read/write on application schemas). A separate, more privileged account is used
    only for manual migration at deployment. Windows authentication with a domain
    service account is preferred when the app server and the database are in the same
    domain; otherwise a SQL login with a strong password supplied by environment
    variable. The connection is encrypted.
4.5 ASP.NET Core Data Protection keys (cookies, antiforgery) are persisted to a
    dedicated folder outside the publish directory, access-limited to the application
    pool identity, and protected with machine-level DPAPI. Otherwise keys would be
    lost on redeploy and all sessions would break.

### 5. File storage
5.1 Uploaded files are stored outside `wwwroot`, outside the publish folder (so a
    redeploy never deletes them), and outside any IIS virtual directory, on a data
    volume provided by infrastructure. The root path is configured by an environment
    variable.
5.2 NTFS permissions: modify only for the application pool identity, plus
    administrators/backup accounts. No anonymous or user access.
5.3 The storage volume is covered by the server's real-time antivirus where
    possible.
5.4 Infrastructure provides a disk quota and a free-space alert (for example at 80%).
    The application limits upload sizes by configuration; IIS request limits match
    them.
5.5 File backups and database backups must be restorable to a consistent point in time
    because the database stores file metadata (see section 8).

### 6. Logging and monitoring
6.1 Application logs are structured and written to a dedicated schema in the
    database. A rolling local file sink is also enabled as a fallback so logs are not
    lost when the database is unavailable.
6.2 Audit records (ADR-0004) are separate from application logs and are never routed
    through the logging pipeline.
6.3 Logs never contain passwords, secrets, tokens, authorization codes, OTP codes,
    full phone numbers or personal payloads. National IDs are masked.
6.4 Retention: application logs are purged by a scheduled job (default 90 days,
    configurable). Audit retention is decided separately.
6.5 Minimal health checks: a liveness endpoint and a readiness endpoint that checks the
    database. They are reachable from inside the data center only and blocked at the
    perimeter.
6.6 Without monitoring tools, operational visibility comes from the logs and from
    scheduled checks by IT. Failures of Sama synchronization and SMS sending are
    logged with a consistent event name so they can be queried.

### 7. Time
7.1 The server clock is synchronized by NTP (required for OTP expiry, cookies and
    token validation).
7.2 All timestamps are stored in UTC. Display uses the Iran time zone and the Jalali
    calendar. The Windows time zone data must be up to date, because Iran stopped
    using daylight saving time.

### 8. Backup and recovery
8.1 Backups of the database and the file storage are performed by the infrastructure
    unit.
8.2 Requirements the application asks from infrastructure (to be agreed, proposed
    values): daily full database backup plus transaction-log backups at least hourly
    (full recovery model), daily file-storage backup, backups kept off the server,
    retention of at least 30 days, and a restore test at least once per term.
8.3 Recovery objectives (maximum data loss and maximum downtime) are agreed with the
    university and recorded; they are not defined in this ADR.
8.4 Before every production migration, a fresh database backup is taken.

### 9. Deployment (manual)
9.1 Deployment is performed manually by the development team on the server administered
    by IT, following a written checklist kept in the repository
    (`docs/deploy-checklist.md`). An optional PowerShell script may help but is not
    required.
9.2 Before publishing, the test suite passes and the build is `Release`.
9.3 Procedure:
    1. Announce a maintenance window (night time is acceptable).
    2. Place `app_offline.htm` in the site folder to show a Persian maintenance page.
    3. Take a database backup.
    4. Apply database changes by running a reviewed idempotent SQL script generated
       from EF Core migrations. The application never applies migrations
       automatically at startup in Production.
    5. Copy the new publish output into a new dated release folder; keep the previous
       release folder.
    6. Point the site to the new release (or replace the content), keeping the data
       folders (files, Data Protection keys) untouched.
    7. Remove `app_offline.htm`, verify the readiness check and a sign-in smoke test.
9.4 Rollback: switch back to the previous release folder. If the database schema
    changed, restore the pre-migration backup, because migrations are forward-only.
9.5 The deployed version (assembly version plus commit identifier) is written to the
    log at startup so the running version is always known.
9.6 Windows Server patching and IIS/OS hardening are the responsibility of IT. The
    application pool runs under a dedicated low-privilege identity, never as an
    administrator. Directory browsing is disabled and server/version headers are
    removed.

### 10. Capacity
10.1 The first release targets one application server. The expected user count and
     peak concurrency are collected from the university before go-live.
10.2 A load test at the expected peak (term start) is run before go-live (Phase 5
     or earlier if the numbers are large). Sama and SMS calls are the likely
     bottlenecks and are addressed by synchronization/caching design (ADR-0003),
     not by hardware alone.
10.3 Scale-out (second server) is not implemented now. It would require moving Data
     Protection keys, file storage and rate-limiting counters to shared stores.

### 11. Vulnerability scanning and the perimeter
11.1 Scans by IT are expected. The application must have security headers, secure
     cookies and no verbose errors so scans do not report avoidable findings.
11.2 The OTP send endpoint is rate-limited per user, per IP and globally, so automated
     scanners cannot exhaust SMS credit. IT is asked to coordinate scans that
     submit forms.

## Alternatives considered
| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Azure (previous proposal) | Managed services, scalability | Not practical for an Iranian university; Sama, SSO and SMS are local; data sensitivity; connectivity | On-premises is the only workable option |
| Other cloud / Iranian hosting | Less hardware | Data location and connectivity to internal Sama/SSO; external exposure of student data | Internal systems are on the university network |
| Linux + Nginx/Kestrel | Lower license cost, good fit for .NET | IT operates Windows; owner decision for IIS | Windows + IIS chosen |
| Windows Service + Kestrel without IIS | Fewer moving parts | No built-in process management, `app_offline`, certificate tooling used by IT | IIS chosen |
| Docker / Kubernetes | Reproducible deployment | No existing infrastructure or skills; manual deployment | Overengineering |
| CI/CD pipeline now | Repeatable releases | No pipeline infrastructure available | Checklist and optional script; can add later |
| SQL Server Express | Free | 10 GB limit, no Agent | Not suitable for production |

## Consequences
- The development team and IT share responsibility; roles for runtime patching,
  certificate renewal, firewall rules and backups must be written down.
- Manual deployment is error-prone. The checklist, app_offline page, pre-migration
  backup and retained previous release are the mitigations.
- Logs in the same database as business data increase its size and load; schema
  separation, retention purge and a file fallback limit the risk.
- Environment variables are readable by server administrators; the server must be
  locked down accordingly.
- Public exposure (section 2.1) increases the attack surface: rate limiting, secure
  cookies, strict authorization (ADR-0004) and perimeter protection are mandatory.
- A single server is a single point of failure; accepted for the first release,
  with backups as the recovery path.

## Acceptance criteria
- The application is reachable over HTTPS on its subdomain from inside and outside
  the campus; HTTP redirects to HTTPS; HSTS is present.
- The database, file storage and health endpoints are not reachable from the Internet.
- No secret exists in source control, `appsettings*.json`, `web.config`, logs or
  documentation.
- A redeploy does not delete uploaded files or invalidate sessions.
- The application never runs EF migrations on startup in Production.
- Production serves a generic Persian error page with no stack trace.
- Logs are written to the database with a working file fallback, and contain no
  OTP codes, tokens or unmasked personal data.
- The OTP stub cannot be enabled outside Development.
- A restore test of database and file storage is completed before go-live.
- Server time is synchronized and timestamps are stored in UTC.
- A rollback procedure has been rehearsed once before go-live.
