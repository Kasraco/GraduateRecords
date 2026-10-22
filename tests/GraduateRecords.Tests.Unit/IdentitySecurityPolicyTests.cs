using GraduateRecords.Infrastructure.Identity;
using GraduateRecords.Infrastructure.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// Proves the decided lockout and password policy are really enforced by Identity
/// (AGENTS §13; owner decisions: 5 attempts / 15 minutes, minimum 14 characters
/// with no composition rules). Uses a plain in-memory <see cref="IUserStore{TUser}"/>,
/// not EF InMemory/SQLite: no persistence behavior is under test here.
/// </summary>
public sealed class IdentitySecurityPolicyTests
{
    private const string LongEnoughWithoutComposition = "abcdefghijklmnop"; // 16 letters, no digit/uppercase/symbol

    private static IdentitySecuritySettings DecidedSettings() => new()
    {
        Lockout = new() { MaxFailedAccessAttempts = 5, LockoutDurationMinutes = 15, AllowedForNewUsers = true },
        Password = new() { RequiredLength = 14 },
        CookieLifetime = new() { IdleMinutes = 30, AbsoluteHours = 8 },
        LoginRateLimit = new() { PermitsPerMinute = 10 },
        Oidc = new() { Authority = "https://idp.test", ClientId = "client", ClientSecret = "secret" },
    };

    [Fact]
    public async Task Passwords_below_fourteen_characters_are_rejected_without_any_composition_rules()
    {
        var users = await CreateUsersAsync();

        var shortResult = await users.CreateAsync(NewUser("1234567890123"), "abcdefghij123"); // 13
        Assert.False(shortResult.Succeeded);
        Assert.Contains(shortResult.Errors, e => e.Code == nameof(IdentityErrorDescriber.PasswordTooShort));

        var decidedResult = await users.CreateAsync(NewUser("2345678901234"), LongEnoughWithoutComposition);
        Assert.True(decidedResult.Succeeded,
            string.Join(", ", decidedResult.Errors.Select(e => e.Code)));
    }

    [Fact]
    public async Task The_fifth_failed_attempt_locks_the_account_for_fifteen_minutes()
    {
        var users = await CreateUsersAsync();
        var user = NewUser("3456789012345");
        Assert.True((await users.CreateAsync(user, LongEnoughWithoutComposition)).Succeeded);

        var before = DateTimeOffset.UtcNow;
        for (var attempt = 1; attempt < 5; attempt++)
        {
            await users.AccessFailedAsync(user);
            Assert.False(await users.IsLockedOutAsync(user), $"Locked after only {attempt} attempts.");
        }

        await users.AccessFailedAsync(user); // 5th failed attempt

        Assert.True(await users.IsLockedOutAsync(user));
        var lockoutEnd = await users.GetLockoutEndDateAsync(user);
        Assert.NotNull(lockoutEnd);
        Assert.InRange(lockoutEnd.Value, before.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(16));
    }

    [Fact]
    public void Lockout_settings_come_from_the_identity_options_not_literals()
    {
        using var services = CreateServiceCollection();
        var options = services.GetRequiredService<IOptions<IdentityOptions>>().Value;

        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Lockout.DefaultLockoutTimeSpan);
        Assert.Equal(14, options.Password.RequiredLength);
    }

    private static ApplicationUser NewUser(string nationalId) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        UserName = nationalId,
        NationalId = nationalId,
        FullName = "کاربر آزمایشی",
        LockoutEnabled = true,
        SecurityStamp = Guid.NewGuid().ToString("N"),
    };

    private static Task<UserManager<ApplicationUser>> CreateUsersAsync() =>
        Task.FromResult(CreateServiceCollection().GetRequiredService<UserManager<ApplicationUser>>());

    private static ServiceProvider CreateServiceCollection()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var settings = DecidedSettings();
        services.AddSingleton<IOptions<IdentitySecuritySettings>>(Options.Create(settings));

        services.AddIdentityCore<ApplicationUser>();
        services.AddScoped<IUserStore<ApplicationUser>, InMemoryUserStore>();

        // Same wiring as production (DependencyInjection.AddIdentityAuthentication).
        services.AddOptions<IdentityOptions>()
            .Configure<IServiceProvider>((options, provider) =>
                IdentityOptionsSetup.Apply(options, provider.GetRequiredService<IOptions<IdentitySecuritySettings>>().Value));

        return services.BuildServiceProvider();
    }

    /// <summary>Minimal store implementing every interface the tested UserManager paths touch.</summary>
    private sealed class InMemoryUserStore : IUserStore<ApplicationUser>,
        IUserPasswordStore<ApplicationUser>,
        IUserLockoutStore<ApplicationUser>,
        IUserSecurityStampStore<ApplicationUser>
    {
        private readonly Dictionary<string, ApplicationUser> _users = new(StringComparer.Ordinal);

        public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.Id);

        public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.UserName);

        public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
        {
            user.UserName = userName;
            return Task.CompletedTask;
        }

        public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.NormalizedUserName);

        public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
        {
            user.NormalizedUserName = normalizedName;
            return Task.CompletedTask;
        }

        public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            _users[user.Id] = user;
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            _users[user.Id] = user;
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            _users.Remove(user.Id);
            return Task.FromResult(IdentityResult.Success);
        }

        public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
            Task.FromResult(_users.TryGetValue(userId, out var user) ? user : null);

        public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken) =>
            Task.FromResult(_users.Values.FirstOrDefault(
                u => string.Equals(u.NormalizedUserName, normalizedUserName, StringComparison.Ordinal)));

        public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken)
        {
            user.PasswordHash = passwordHash;
            return Task.CompletedTask;
        }

        public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.PasswordHash);

        public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.PasswordHash is not null);

        public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.LockoutEnd);

        public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
        {
            user.LockoutEnd = lockoutEnd;
            return Task.CompletedTask;
        }

        public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            user.AccessFailedCount++;
            return Task.FromResult(user.AccessFailedCount);
        }

        public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
        {
            user.AccessFailedCount = 0;
            return Task.CompletedTask;
        }

        public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.AccessFailedCount);

        public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.LockoutEnabled);

        public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken cancellationToken)
        {
            user.LockoutEnabled = enabled;
            return Task.CompletedTask;
        }

        public Task SetSecurityStampAsync(ApplicationUser user, string? stamp, CancellationToken cancellationToken)
        {
            user.SecurityStamp = stamp;
            return Task.CompletedTask;
        }

        public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken cancellationToken) =>
            Task.FromResult(user.SecurityStamp);

        public void Dispose() { }
    }
}
