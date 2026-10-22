using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;

namespace GraduateRecords.Tests.Unit;

public sealed class AbsoluteSessionLifetimeTests
{
    private static readonly DateTimeOffset Issued = new(2026, 1, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Cap = TimeSpan.FromHours(8);

    [Fact]
    public void A_session_anchored_at_sign_in_is_valid_for_the_duration_of_the_cap()
    {
        var properties = new AuthenticationProperties();
        AbsoluteSessionLifetime.Anchor(properties, Issued);

        Assert.True(AbsoluteSessionLifetime.IsWithinAbsoluteCap(properties, Issued, Cap));
        Assert.True(AbsoluteSessionLifetime.IsWithinAbsoluteCap(properties, Issued.AddHours(7), Cap));
        Assert.True(AbsoluteSessionLifetime.IsWithinAbsoluteCap(properties, Issued.AddHours(8), Cap));
        Assert.False(AbsoluteSessionLifetime.IsWithinAbsoluteCap(properties, Issued.AddHours(8).AddSeconds(1), Cap));
    }

    [Fact]
    public void Missing_or_malformed_anchors_fail_closed()
    {
        Assert.False(AbsoluteSessionLifetime.IsWithinAbsoluteCap(new AuthenticationProperties(), Issued, Cap));

        var malformed = new AuthenticationProperties();
        malformed.Items[AbsoluteSessionLifetime.AnchorPropertyKey] = "not-a-date";
        Assert.False(AbsoluteSessionLifetime.IsWithinAbsoluteCap(malformed, Issued, Cap));
    }

    [Fact]
    public void An_anchor_in_the_future_or_a_non_positive_cap_fails_closed()
    {
        var future = new AuthenticationProperties();
        AbsoluteSessionLifetime.Anchor(future, Issued.AddMinutes(5));
        Assert.False(AbsoluteSessionLifetime.IsWithinAbsoluteCap(future, Issued, Cap));

        var anchored = new AuthenticationProperties();
        AbsoluteSessionLifetime.Anchor(anchored, Issued);
        Assert.False(AbsoluteSessionLifetime.IsWithinAbsoluteCap(anchored, Issued, TimeSpan.Zero));
    }

    [Fact]
    public void The_anchor_survives_repeated_reads_of_the_same_properties()
    {
        var properties = new AuthenticationProperties();
        AbsoluteSessionLifetime.Anchor(properties, Issued);
        AbsoluteSessionLifetime.Anchor(properties, Issued); // idempotent overwrite

        Assert.True(AbsoluteSessionLifetime.IsWithinAbsoluteCap(properties, Issued.AddHours(1), Cap));
    }
}
