# ADR-0008: Data Protection key storage and cookie/session protection

- **Status:** Accepted
- **Date:** 2026-10-18
- **Deciders:** Project Owner
- **Related documents/ADRs:** ADR-0001 (authentication), ADR-0002 (deactivation), ADR-0006 (hosting; section 4.5 is superseded by this ADR), ADR-0007 (SQL Server)

## Context
ASP.NET Core Data Protection encrypts and signs authentication cookies, antiforgery tokens and TempData. Its keys must survive application restarts and redeployments, and must be protected at rest.

Facts: one application server, no second server planned. The IIS application pool uses the default pool identity (no domain service account). University IT can issue a certificate to protect the keys. The university has no session or key-rotation policy, so ASP.NET Core defaults are used where sensible. Azure Key Vault is not available (on-premises hosting, ADR-0006).

Important IIS fact: by default the Data Protection "application discriminator" is
- **Date:** 2026-10-18
- **Deciders:** Project Owner
- **Related documents/ADRs:** ADR-0001 (authentication), ADR-0002 (deactivation),
  ADR-0006 (hosting; section 4.5 is superseded by this ADR), ADR-0007 (SQL Server)

## Context
ASP.NET Core Data Protection encrypts and signs authentication cookies, antiforgery
tokens and TempData. Its keys must survive application restarts and redeployments,
and must be protected at rest.

Facts: one application server, no second server planned. The IIS application pool
uses the default pool identity (no domain service account). University IT can issue a
certificate to protect the keys. The university has no session or key-rotation policy,
so ASP.NET Core defaults are used where sensible. Azure Key Vault is not available
(on-premises hosting, ADR-0006).

Important IIS fact: by default the Data Protection "application discriminator" is
the content root path. ADR-0006 deploys into new dated release folders, so without an
explicit application name every release would invalidate all cookies and tokens.

## Decision

### 1. Key ring storage
1.1 The key ring is persisted in the application's SQL Server database
    (table `DataProtectionKeys`, created by an EF Core migration), using the EF Core
    Data Protection integration implemented in Infrastructure.
1.2 Rationale: the database is already backed up by the infrastructure unit, survives
    redeployments, needs no extra folder, drive or file permissions, and is restored
    consistently with the rest of the data.
1.3 Registry or user-profile key storage (the IIS default) is not used because it is
    tied to the machine and not backed up.
1.4 Production and Development use separate databases and therefore separate key rings.

### 2. Key encryption at rest (certificate)
2.1 Keys are encrypted with an X.509 certificate issued by university IT, using
    `ProtectKeysWithCertificate`. DPAPI is not used for Production.
2.2 This is a dedicated certificate for key protection. It is not the HTTPS/TLS
    certificate, and the TLS certificate is never reused for this purpose.
2.3 Certificate requirements (to be requested from IT): RSA key of at least 2048 bits
    (3072 preferred), validity of several years, private key present.
2.4 The certificate is installed in the `LocalMachine\My` store of the application
    server. The IIS application pool identity (`IIS AppPool\<pool name>`) is granted
    read access to the certificate's private key only. The application loads it by
    thumbprint; the thumbprint is configuration (environment variable), not code.
    No PFX file and no certificate password is stored in the repository, publish
    folder or configuration.
2.5 IT keeps a secure backup of the certificate with its private key outside the
    server. If the certificate is lost, the key ring cannot be read and must be
    recreated (see 6.3).
2.6 Renewal: when the certificate is replaced, the new certificate is used to encrypt
    new keys, and the old certificate stays installed and listed for decryption
    (`UnprotectKeysWithAnyCertificate`) for at least 120 days after the switch (key
    lifetime plus maximum session lifetime). It is removed only after that.
2.7 The application writes a Warning log at startup, and once per day, when the
    certificate expires in fewer than 90 days. This is the early warning since no
    monitoring tool exists. IT also records the expiry date.
2.8 In Production the application refuses to start if the certificate is not
    configured or cannot be loaded. In Development the key ring may be stored without
    certificate encryption (Development environment only).

### 3. Application name and key lifetime
3.1 `SetApplicationName("GraduateRecords")` is set explicitly so that cookies and
    keys remain valid across release folders and redeployments.
3.2 Automatic key management stays enabled. Default key lifetime is 90 days, set
    explicitly in configuration (`SetDefaultKeyLifetime`), new keys are generated
    automatically before expiry.
3.3 Keys are never deleted manually in normal operation. Rule 6.3 describes the only
    exception.

### 4. What Data Protection may protect
4.1 Data Protection is used only for short-lived framework artifacts: authentication
    cookie, antiforgery tokens, TempData, and similar. Long-lived business data
    (database values, files, secrets, OTP codes) is never encrypted with
    `IDataProtector`, because losing keys would then mean losing data.
4.2 OTP codes are hashed (ADR-0002), not encrypted with Data Protection.

### 5. Cookies and session
5.1 Authentication cookie: `HttpOnly`, `Secure`, `SameSite=Lax` (consistent with
    ADR-0001), a fixed project-specific name, sliding expiration.
5.2 Defaults for the first release (configurable, not hard-coded): idle timeout 60
    minutes with sliding expiration. This deliberately replaces the ASP.NET Core
    Identity default of 14 days, which is too long for sensitive student data. When the
    session expires, the user is redirected to SSO and returns, which is cheap.
5.3 Security stamp validation interval is set so a deactivated user is blocked on the
    very next request (ADR-0002 rule 7.2), by validating on every request. A database
    lookup by primary key per request is acceptable at the expected scale and is
    reviewed in the load test (ADR-0006).
5.4 Antiforgery uses Data Protection and a project-specific cookie name. All
    state-changing endpoints validate antiforgery tokens (global auto-validation filter).
5.5 Logout clears the local cookie and redirects to the SSO end-session endpoint
    (ADR-0001).

### 6. Operations and recovery
6.1 No extra folder, drive or file backup is needed for keys. They are inside the
    database backup. The certificate with private key is backed up by IT separately.
6.2 Server rebuild: restore the database, install the same certificate with its
    private key, grant the new application pool identity access to it. Existing
    sessions keep working. If the certificate cannot be restored, see 6.3.
6.3 Last-resort recovery if keys cannot be decrypted (certificate lost): clear the
    `DataProtectionKeys` table and install a new certificate. Effect: all users are
    signed out and must sign in again through SSO, and open forms with old antiforgery
    tokens must be reloaded. No business data is lost (rule 4.1). This operation is
    audited and documented in the deployment checklist.
6.4 The restore test required by ADR-0006 includes verifying sign-in after restoring
    the database and certificate on a clean machine.

## Alternatives considered
| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Azure Key Vault (previous proposal) | Managed secrets | Not available on-premises for this university | Not applicable |
| Key ring in database + certificate (chosen) | Backed up with database, survives redeploys, no folder, restoreable | Needs certificate management | Chosen |
| Key ring in folder + DPAPI | Simple | Tied to one machine, needs ACLs and file backup | Fallback only |
| Key ring in folder + certificate | Machine independent | Extra folder, ACLs, backup scope | Database storage is simpler |
| IIS default registry storage | Zero setup | Not backed up, machine-bound | Rejected |
| Shared network folder | Multi-server ready | No second server planned | Unneeded complexity |
| HSM / external vault server | Strong protection | No infrastructure | Overengineering |
| Ephemeral keys (no persistence) | None | All sessions break on every restart | Rejected |
| Reuse the TLS certificate for key protection | One certificate | Different lifecycle and exposure | Dedicated certificate |

## Consequences
- A certificate lifecycle (issue, install, ACL, backup, renew) must be owned by IT with
  the development team, and recorded in the deployment checklist.
- The `DataProtectionKeys` table is part of the schema and the database restore.
- Application pool identity changes would require re-granting private key access.
- Cookie idle timeout of 60 minutes may interrupt very long form sessions. Draft
  saving (Phase 3) reduces the impact.
- Per-request security stamp validation adds one primary-key database lookup per
  request.

## Acceptance criteria
- Cookies and antiforgery tokens remain valid after an application restart and after
  a deployment into a new release folder.
- Production fails to start without a loadable key-protection certificate.
- No PFX file, password or private key exists in source control, publish output or
  configuration.
- The key ring in the database contains encrypted key material only (no clear-text
  key elements).
- The application logs a warning when the certificate expires in fewer than 90 days.
- A deactivated user is rejected on the next request.
- After a database restore on a clean machine with the certificate installed, existing
  sessions still work.
- No business data is encrypted with `IDataProtector`.
- Development and Production key rings are separate.