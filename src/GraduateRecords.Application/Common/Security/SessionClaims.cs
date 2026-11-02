namespace GraduateRecords.Application.Common.Security;

/// <summary>
/// Claim types that describe the session principal itself (not authorization).
/// <para>
/// The display-name claim lives here because both the Infrastructure principal
/// factory (which writes it) and the Web <c>ICurrentUser</c> implementation
/// (which reads it) must agree on it, and Web must not reference Infrastructure
/// (AGENTS §2). The factory keeps its existing constant name as an alias, so no
/// call site or test changes behaviour.
/// </para>
/// </summary>
public static class SessionClaims
{
    /// <summary>Display-name snapshot claim (audit <c>ActorDisplayName</c> source).</summary>
    public const string FullName = "full_name";
}
