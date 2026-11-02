namespace GraduateRecords.Application.Common.Security;

/// <summary>
/// The authenticated actor as seen by Application handlers (ARCHITECTURE §15:
/// "identity from <c>ICurrentUser</c> only" — a handler never reads request data
/// for identity, and the Web layer never passes an actor id into a command).
/// <para>
/// Deliberately exposes <b>only</b> role/permission facts: neither
/// <see cref="Common.Identity.LocalUserKind"/> nor the authentication method is
/// available here, so no authorization decision made by a handler can read them
/// (ADR-0017 §1, §4, §21). <see cref="StaffScope"/> is derived from role names
/// only (ADR-0017 StaffScope rule).
/// </para>
/// </summary>
public interface ICurrentUser
{
    /// <summary>True when a session principal exists.</summary>
    bool IsAuthenticated { get; }

    /// <summary>Stable identity reference (the local user id) — audit <c>ActorUserId</c>.</summary>
    string? UserId { get; }

    /// <summary>Display-name snapshot of the current session — audit <c>ActorDisplayName</c>.</summary>
    string? DisplayName { get; }

    /// <summary>Canonical role names carried by the session principal.</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>Permission values carried by the session principal (role claims).</summary>
    IReadOnlyCollection<string> Permissions { get; }

    /// <summary>
    /// Fail-closed permission check: an unknown/unregistered permission is never
    /// granted (ADR-0017 §21).
    /// </summary>
    bool HasPermission(string permission);

    /// <summary>Phase 1 scope derived from role names (ADR-0017 StaffScope rule).</summary>
    StaffScope StaffScope { get; }
}
