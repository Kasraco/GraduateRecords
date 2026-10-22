using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Infrastructure.Options;
using Microsoft.AspNetCore.Identity;

namespace GraduateRecords.Tests.Unit;

public sealed class IdentitySecuritySettingsTests
{
    private static IdentitySecuritySettings DecidedConfiguration() => new()
    {
        Lockout = new() { MaxFailedAccessAttempts = 5, LockoutDurationMinutes = 15, AllowedForNewUsers = true },
        Password = new() { RequiredLength = 14 },
        CookieLifetime = new() { IdleMinutes = 30, AbsoluteHours = 8 },
        LoginRateLimit = new() { PermitsPerMinute = 10 },
        Oidc = new()
        {
            Authority = "https://idp.ajums.ac.ir",
            ClientId = "graduate-records",
            ClientSecret = "from-secret-configuration",
            UsePkce = true,
        },
    };

    [Fact]
    public void The_decided_configuration_is_valid() => Assert.True(DecidedConfiguration().IsValid());

    [Fact]
    public void An_unconfigured_identity_section_fails_closed()
    {
        // Defaults are deliberately zero/empty so a missing configuration block never starts.
        Assert.False(new IdentitySecuritySettings().IsValid());
    }

    [Theory]
    [InlineData(0, 15)] // no attempts allowed
    [InlineData(5, 0)] // no lockout duration
    [InlineData(-1, 15)]
    public void Lockout_settings_must_be_positive(int attempts, int minutes)
    {
        var settings = DecidedConfiguration();
        settings.Lockout.MaxFailedAccessAttempts = attempts;
        settings.Lockout.LockoutDurationMinutes = minutes;

        Assert.False(settings.IsValid());
    }

    [Fact]
    public void A_password_length_below_fourteen_is_invalid()
    {
        var settings = DecidedConfiguration();
        settings.Password.RequiredLength = 13;
        Assert.False(settings.IsValid());

        settings.Password.RequiredLength = IdentitySecuritySettings.MinimumPasswordLength;
        Assert.True(settings.IsValid());
    }

    [Fact]
    public void Cookie_idle_and_absolute_cap_settings_are_validated_together()
    {
        var settings = DecidedConfiguration();
        settings.CookieLifetime.IdleMinutes = 0;
        Assert.False(settings.IsValid());

        settings = DecidedConfiguration();
        settings.CookieLifetime.AbsoluteHours = 1; // 60 minutes >= 30 minutes idle -> still fine
        Assert.True(settings.IsValid());

        settings.CookieLifetime.IdleMinutes = 90; // idle longer than the absolute cap makes no sense
        Assert.False(settings.IsValid());
    }

    [Fact]
    public void Rate_limit_settings_cannot_be_zero()
    {
        var settings = DecidedConfiguration();
        settings.LoginRateLimit.PermitsPerMinute = 0;
        Assert.False(settings.IsValid());
    }

    [Theory]
    [InlineData("http://idp.ajums.ac.ir")] // not https
    [InlineData("idp.ajums.ac.ir")] // not absolute
    [InlineData("")]
    public void The_sso_authority_must_be_an_absolute_https_uri(string authority)
    {
        var settings = DecidedConfiguration();
        settings.Oidc.Authority = authority;
        Assert.False(settings.IsValid());
    }

    [Fact]
    public void The_sso_client_secret_must_come_from_configuration()
    {
        var settings = DecidedConfiguration();
        settings.Oidc.ClientSecret = string.Empty;
        Assert.False(settings.IsValid());
    }

    [Fact]
    public void Identity_options_are_applied_exactly_as_decided()
    {
        var options = new IdentityOptions();
        IdentityOptionsSetup.Apply(options, DecidedConfiguration());

        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Lockout.DefaultLockoutTimeSpan);
        Assert.True(options.Lockout.AllowedForNewUsers);

        Assert.Equal(14, options.Password.RequiredLength);
        Assert.False(options.Password.RequireDigit);
        Assert.False(options.Password.RequireLowercase);
        Assert.False(options.Password.RequireUppercase);
        Assert.False(options.Password.RequireNonAlphanumeric);
    }
}
