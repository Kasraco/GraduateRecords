using GraduateRecords.Application.Common.Messaging;

namespace GraduateRecords.Application.Features.Administration.Bootstrap;

/// <summary>
/// Result of one Bootstrap invocation (ADR-0016 §4; approved decisions D1/D2).
/// Every value is a successful, non-throwing outcome — failures arrive as
/// <c>Result</c> errors so the composition root can decide whether startup may
/// continue (Development) or must fail closed (everywhere else).
/// </summary>
public enum BootstrapOutcome
{
    /// <summary>The first Administrator identity was created and given the role (one transaction).</summary>
    Created,

    /// <summary>The configured identity already existed; only the missing Administrator role was established (§4.5 resume).</summary>
    RoleEstablished,

    /// <summary>
    /// An effective Administrator already existed when the locked re-check ran:
    /// completely inert — no writes, no audit, startup proceeds (ADR-0016 §4.2,
    /// decision D2).
    /// </summary>
    AlreadyEstablished,

    /// <summary>
    /// Development with no Bootstrap configuration: startup proceeds for
    /// inspection/testing only — no writes, no privilege, no credential, no
    /// session (ADR-0016 §8.1/§14.2).
    /// </summary>
    SkippedDevelopment,
}

/// <summary>
/// Startup Bootstrap of the first Administrator (ADR-0016 §1–§4). Configuration
/// values are carried in the command by the composition root (Program.cs), so
/// the Application layer never references Microsoft.Extensions.Options or reads
/// configuration itself (AGENTS §2). The command holds identity information
/// only — never a password, token or other credential (ADR-0016 §2.2).
/// </summary>
public sealed record BootstrapAdministratorCommand(
    string? NationalId,
    string? MobileNumber,
    string EnvironmentName) : ICommand<BootstrapOutcome>;
