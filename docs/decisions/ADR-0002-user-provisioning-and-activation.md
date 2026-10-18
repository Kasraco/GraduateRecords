# ADR-0002: User provisioning, activation, verification and deactivation

- **Status:** Accepted
- **Date:** 2026-10-18
- **Deciders:** Project Owner
- **Related documents/ADRs:** ADR-0001 (authentication model), Sama web service
  documentation v8.0, Melipayamak SendOtp documentation, future ADR-0003
  (external integrations and student synchronization), future ADR on
  Roles/Permissions

## Context
Authentication is performed only by the university SSO (ADR-0001). A person is
identified by Iranian National ID (10-digit string). There is no self-registration
and no email confirmation.

Two kinds of people use the system:
- Students: known to the university education system (Sama), reachable through
  its web services.
- Graduate-studies staff and administrators: not in Sama, created by an
  Administrator.

Every student has a Sama `StudentStatus`, and that status decides whether the
student's account is usable. Mobile numbers come from Sama (`GetContactInfo`)
for students and from the Administrator for staff. Account verification uses an
SMS OTP sent through the Melipayamak gateway.

## Decision

### 1. Registration
1.1 No self-registration route or page exists.
1.2 Local users are created only by (a) an Administrator, or (b) just-in-time
    provisioning of students at first successful SSO login.
1.3 Having a valid SSO account alone never grants access.

### 2. Staff and administrators
2.1 An Administrator creates the user with: National ID, full name, role(s),
    mobile number. Creation is audited.
2.2 The first Administrator is seeded from configuration (National ID and mobile
    number only; no passwords or secrets). The seeded user still signs in through
    SSO and passes OTP verification.
2.3 Role assignment is local only (ADR-0001 rule 8).

### 3. Students (just-in-time provisioning)
3.1 After a successful SSO login, if no local user exists for the National ID,
    the system queries Sama by National ID
    (`GetStudentPersonInfoListByNationalCode`).
3.2 A user with role Student is created only if at least one returned record has
    `StudyLevel.IsPostGraduate = true`. Otherwise no user is created, access is
    denied with a friendly message, and the denial is audited.
3.3 A student can have several Sama records (several student numbers, e.g.
    master's and PhD). All records are stored as academic records linked to the
    single user; the user is unique per National ID.
3.4 Mobile number is read from Sama `GetContactInfo`. If missing or invalid the
    user stays Pending and sees a friendly message.

### 4. Access state
Effective access to the application requires all three: CanUseSystem = IsActive AND IsEligible AND PhoneVerified
- `IsActive`: set only by Administrators (deactivate/reactivate). Default true
  at creation.
- `IsEligible`: derived from Sama `StudentStatus` through the admin-managed
  `StudentStatusPolicy` table (Sama status code -> `GrantsAccess` yes/no).
  Staff and administrators are always eligible (no Sama status).
- `PhoneVerified`: true after a successful OTP verification (`PhoneVerifiedAt`).

Rules:
4.1 Synchronization changes only `IsEligible` and academic data. It never changes
    `IsActive`. A user deactivated by an Administrator is not reactivated by sync.
4.2 An unknown Sama status code is treated as not eligible, and an audit event
    is written so an Administrator can add it to `StudentStatusPolicy`.
4.3 `StudentStatusPolicy` is data, not code. Allowed statuses are never
    hard-coded.
4.4 If Sama is unreachable, the last known `IsEligible` value is used. The last
    successful sync time is stored (`LastSyncedAt`, `LastSyncStatus`,
    `LastSyncError`). An optional maximum staleness period may be configured
    later; it is not implemented in the first release.
4.5 Eligibility is re-evaluated at each SSO login when Sama is reachable, and by
    a periodic background synchronization.

### 5. Sama integration (rules for this ADR; details in ADR-0003)
5.1 Use the REST endpoint of the Sama services over HTTPS or a closed internal
    network. Do not use WCF/SOAP clients.
5.2 Use a typed `HttpClient` with timeout, cancellation tokens and a bounded
    retry for transient failures only. Never retry indefinitely.
5.3 The Sama service account (for services requiring `AuthenticationService`
    login and cookie) is stored only in the secret store.
5.4 If Sama is down: an already provisioned student can sign in with the last
    known state. An unprovisioned student cannot sign in and sees a friendly
    message ("your information is currently unavailable").
5.5 Never expose technical errors to users. Log structured details server-side
    without National ID or personal payloads.

### 6. OTP verification (Melipayamak)
6.1 A user in Pending state must verify the mobile number by OTP before any
    feature is accessible. After success, OTP is not requested again unless the
    mobile number changes.
6.2 Mobile number is never supplied by the user during verification. It is
    normalized to `09xxxxxxxxx`.
6.3 Code: 6 digits in the range 100000-999999 (the provider's `code` parameter
    is an integer), generated with a cryptographically secure RNG, stored hashed,
    single use, short expiry (a few minutes), limited attempts, rate-limited
    resend per user and per IP.
6.4 The gateway is hidden behind an abstraction in Application and implemented in
    Infrastructure, so the provider can be replaced.
6.5 Provider credentials (username and API key/password) live only in the secret
    store. The server's outbound IP must be whitelisted in the provider panel.
6.6 Provider return values are mapped to internal results. Success only when the
    provider returns a send id; any unknown value is a failure. Raw provider
    codes are never shown to users.
6.7 If sending fails (provider down, IP not allowed, blacklisted number, no
    credit), the user stays Pending and sees a friendly message. There is no
    bypass path for ordinary users.
6.8 OTP codes and full phone numbers are never logged or audited. Masked numbers
    only.

### 7. Deactivation and reactivation
7.1 An Administrator can deactivate and reactivate any user. Both actions are
    audited with actor and reason.
7.2 A deactivated user cannot sign in, and existing sessions stop working: the
    Identity Security Stamp is rotated and validated on every request.
7.3 An Administrator cannot deactivate the last active Administrator.

### 8. Data retention
8.1 Users with submissions, workflow history or audit history are never
    hard-deleted. They are deactivated.

### 9. Audit events (minimum)
User created by admin; student provisioned from Sama; provisioning denied
(unknown/ineligible National ID); eligibility changed; unknown status code
encountered; OTP sent; OTP verified; OTP failed; OTP locked; user activated;
user deactivated; role granted/revoked. Audit entries contain no OTP code, no
token, no password and no unmasked phone number.

## Data model (conceptual)
- `ApplicationUser` (Identity): `UserName` = National ID, `NationalId` (unique),
  `FullName`, `PhoneNumber`, `PhoneVerifiedAt`, `IsActive`, `IsEligible`,
  `UserKind` (Student / Staff).
- `StudentAcademicRecord`: user link, Sama student number, faculty, course of
  study, sub-course, study level, entry term, Sama status code/title,
  start date, stop date.
- `StudentStatusPolicy`: Sama status code (unique), title, `GrantsAccess`.
- `OtpChallenge`: user link, code hash, expiry, attempt count, created at,
  consumed at.
- Sync metadata: `LastSyncedAt`, `LastSyncStatus`, `LastSyncError`.

## Flows
**Student first login:** SSO callback -> validate National ID -> local user exists?
- Yes: go to access-state check.
- No: query Sama -> graduate record exists? -> create user (Pending, eligibility
  from policy) -> send OTP -> verify -> active.
- Sama down: friendly message, audit.

**Subsequent login:** SSO callback -> user found -> IsActive? -> refresh
eligibility from Sama if reachable (otherwise last known) -> IsEligible? ->
PhoneVerified? -> sign in.

**Staff first login:** SSO callback -> user created by admin exists -> PhoneVerified?
(OTP if not) -> sign in.

## Alternatives considered
| Option | Pros | Cons | Why not chosen |
|---|---|---|---|
| Self-registration + email confirmation | Familiar pattern | Conflicts with SSO-only; email is not identity; spam risk | Identity is proven by SSO |
| Admin creates all users, including students | Tight control | Heavy burden; duplicates Sama data | Students are known from Sama |
| Provision any SSO user | Simplest | Any university account gets in | Violates least privilege |
| OTP at every login | Extra factor | Poor UX; SSO already authenticates | OTP only for verification |
| One `IsActive` flag mixing admin and Sama state | Simple | Sync could override an admin decision | Separate flags are safer |
| JIT students from Sama + admin-created staff (chosen) | Automatic for students; controlled for staff | Depends on Sama and SMS for new students | Chosen |

## Consequences
- New entities: `StudentStatusPolicy`, `OtpChallenge`, `StudentAcademicRecord`.
- An admin screen manages staff users, activation state and `StudentStatusPolicy`.
- First-time student login depends on Sama and the SMS gateway availability.
  Already verified users are unaffected by their outage.
- Deployment must register the outbound IP in the Melipayamak panel and allow
  outbound HTTPS to the provider and to Sama.
- Deactivation is enforced server-side on every request.
- The meaning of Sama status codes must be agreed with the education office and
  loaded into `StudentStatusPolicy` before go-live.

## Acceptance criteria
- No self-registration route exists.
- A National ID that is neither created by an Administrator nor a graduate
  student in Sama never gets a user.
- A Pending user cannot access any feature until OTP verification succeeds.
- OTP is single-use, expires, is attempt-limited and resend-rate-limited, and the
  generated code never has a leading zero.
- Mobile number cannot be changed or supplied by the user during verification.
- Sync cannot reactivate a user deactivated by an Administrator.
- An unknown Sama status leads to a non-eligible user plus an audit event.
- A deactivated user is blocked on the next request, including existing sessions.
- The last active Administrator cannot be deactivated.
- An already provisioned student can sign in while Sama is down.
- No OTP code, token, secret or full phone number appears in logs, audit records
  or source control.