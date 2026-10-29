using Microsoft.AspNetCore.Authentication;

namespace GraduateRecords.Infrastructure.Identity;

/// <summary>
/// ADR-0015 §15/§16: remembers which authentication path issued the current
/// session. The marker lives in <see cref="AuthenticationProperties.Items"/>,
/// exactly like the absolute-session anchor, because the security-stamp
/// validator rebuilds the session principal from the store on every request —
/// any claim added outside the claims factory would be dropped. The marker is
/// an audit/observability attribute only: it never influences authorization
/// decisions (ADR-0015 §5).
/// </summary>
public static class AuthenticationMethodMarker
{
    public const string PropertyKey = "Identity.AuthenticationMethod";

    /// <summary>University SSO (OIDC) session.</summary>
    public const string Sso = "sso";

    /// <summary>Local National ID + password session (Staff/Administrator only).</summary>
    public const string Local = "local";

    public static void Mark(AuthenticationProperties properties, string method)
    {
        ArgumentNullException.ThrowIfNull(properties);
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        properties.Items[PropertyKey] = method;
    }

    /// <summary>
    /// The marked method, or null when the ticket carries no marker (tickets
    /// issued before this mechanism existed are treated as SSO sessions).
    /// </summary>
    public static string? Get(AuthenticationProperties? properties)
    {
        if (properties is null)
        {
            return null;
        }

        return properties.Items.TryGetValue(PropertyKey, out var method)
                   && !string.IsNullOrWhiteSpace(method)
            ? method
            : null;
    }
}
