using System.Globalization;
using Microsoft.AspNetCore.Authentication;

namespace GraduateRecords.Infrastructure.Identity;

/// <summary>
/// ADR-0001 rule 13: a bounded lifetime with sliding expiration. The sliding
/// cookie renewal resets the ticket's IssuedUtc, so the absolute session cap is
/// anchored in a property written once at sign-in and never refreshed. Missing or
/// malformed anchors are rejected (fail-closed).
/// </summary>
public static class AbsoluteSessionLifetime
{
    public const string AnchorPropertyKey = "Identity.AbsoluteSessionStartUtc";

    private const string RoundTripFormat = "O";

    public static void Anchor(AuthenticationProperties properties, DateTimeOffset issuedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(properties);
        properties.Items[AnchorPropertyKey] = issuedAtUtc.UtcDateTime.ToString(RoundTripFormat, CultureInfo.InvariantCulture);
    }

    public static bool IsWithinAbsoluteCap(AuthenticationProperties properties, DateTimeOffset nowUtc, TimeSpan absoluteCap)
    {
        ArgumentNullException.ThrowIfNull(properties);

        if (absoluteCap <= TimeSpan.Zero)
        {
            return false;
        }

        if (!properties.Items.TryGetValue(AnchorPropertyKey, out var raw)
            || string.IsNullOrWhiteSpace(raw)
            || !DateTimeOffset.TryParseExact(raw, RoundTripFormat, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var start))
        {
            return false;
        }

        if (start > nowUtc)
        {
            return false; // Anchored in the future: tampering or clock trouble -> fail closed.
        }

        return nowUtc - start <= absoluteCap;
    }
}
