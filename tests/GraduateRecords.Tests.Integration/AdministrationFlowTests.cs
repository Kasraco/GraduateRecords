using System.Net;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Domain.Auditing;
using GraduateRecords.Domain.Common;
using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// UC3 end-to-end behaviour over HTTP: Post-Redirect-Get flows, the Persian
/// toasts they carry, the best-effort Phase 1A security events (ADR-0017 §22),
/// the §20 self-role-change ban and the §17 last-Administrator invariant.
/// </summary>
[Collection(WebApplicationCollection.Name)]
public sealed class AdministrationFlowTests(GraduateRecordsWebApplicationFactory factory)
{
    [Fact]
    public async Task Creating_a_user_redirects_to_its_details_and_writes_a_user_created_event()
    {
        var administrator = await CreateAdministratorAsync();
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, administrator);
        var (antiforgeryCookie, antiforgeryToken) = await AdministrationTestFlow.AntiforgeryAsync(factory, administrator);
        var correlationId = NextCorrelationId();
        var nationalId = SsoTestFlow.NextNationalId();

        using var response = await AdministrationTestFlow.PostAsync(
            client,
            "/Admin/Users/Create",
            new Dictionary<string, string>
            {
                ["NationalId"] = nationalId,
                ["FullName"] = "کارمند جدید",
                ["MobileNumber"] = "۰۹۱۲۳۴۵۶۷۸۹",
                ["Kind"] = nameof(LocalUserKind.Staff),
                ["Roles"] = RoleNames.GraduateStaff,
            },
            cookie,
            antiforgeryCookie,
            antiforgeryToken,
            correlationId);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Admin/Users/Details", response.Headers.Location!.ToString(), StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var created = await userManager.FindByNameAsync(nationalId);
        Assert.NotNull(created);
        Assert.Equal(LocalUserKind.Staff, created!.UserKind);
        Assert.Equal("09123456789", created.PhoneNumber); // canonical digits reached the store
        Assert.Contains(RoleNames.GraduateStaff, await userManager.GetRolesAsync(created));
        Assert.Null(created.PasswordHash); // ADR-0016 §7: provisioning creates no credential

        var audit = Assert.Single(SsoTestFlow.AuditEvents(
            factory, AuditActions.AuthorizationUserCreated, correlationId));
        Assert.True(SsoTestFlow.AuditEquals(audit, "Success"));
        Assert.False(
            SsoTestFlow.AuditContains(audit, nationalId),
            "The National ID must never reach an audit payload (AGENTS §14).");
    }

    [Fact]
    public async Task Granting_a_role_redirects_with_a_toast_and_writes_a_role_granted_event()
    {
        var target = await AdministrationTestFlow.CreateUserAsync(factory, "کاربر هدف", LocalUserKind.Staff);
        var administrator = await CreateAdministratorAsync();
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, administrator);
        var (antiforgeryCookie, antiforgeryToken) = await AdministrationTestFlow.AntiforgeryAsync(factory, administrator);
        var correlationId = NextCorrelationId();

        using var response = await AdministrationTestFlow.PostAsync(
            client,
            "/Admin/Roles/Grant",
            new Dictionary<string, string>
            {
                ["id"] = target.Id,
                ["roleName"] = RoleNames.GraduateStaff,
            },
            cookie,
            antiforgeryCookie,
            antiforgeryToken,
            correlationId);

        var page = await AdministrationTestFlow.FollowAsync(client, response, cookie);
        Assert.Contains(OnPage("نقش با موفقیت اعطا شد."), page, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Contains(RoleNames.GraduateStaff, await userManager.GetRolesAsync(target));

        var audit = Assert.Single(SsoTestFlow.AuditEvents(
            factory, AuditActions.AuthorizationRoleGranted, correlationId));
        Assert.True(SsoTestFlow.AuditContains(audit, RoleNames.GraduateStaff));
    }

    [Fact]
    public async Task Revoking_a_role_redirects_with_a_toast_and_writes_a_role_revoked_event()
    {
        var target = await AdministrationTestFlow.CreateUserAsync(
            factory, "کاربر هدف", LocalUserKind.Staff, RoleNames.GraduateStaff);
        var administrator = await CreateAdministratorAsync();
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, administrator);
        var (antiforgeryCookie, antiforgeryToken) = await AdministrationTestFlow.AntiforgeryAsync(factory, administrator);
        var correlationId = NextCorrelationId();

        using var response = await AdministrationTestFlow.PostAsync(
            client,
            "/Admin/Roles/Revoke",
            new Dictionary<string, string>
            {
                ["id"] = target.Id,
                ["roleName"] = RoleNames.GraduateStaff,
            },
            cookie,
            antiforgeryCookie,
            antiforgeryToken,
            correlationId);

        var page = await AdministrationTestFlow.FollowAsync(client, response, cookie);
        Assert.Contains(OnPage("نقش با موفقیت حذف شد."), page, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.DoesNotContain(RoleNames.GraduateStaff, await userManager.GetRolesAsync(target));

        Assert.Single(SsoTestFlow.AuditEvents(
            factory, AuditActions.AuthorizationRoleRevoked, correlationId));
    }

    /// <summary>ADR-0017 §20 / ADR-0004 §1.3: an Administrator cannot change their own roles.</summary>
    [Fact]
    public async Task An_administrator_cannot_change_their_own_roles()
    {
        var administrator = await CreateAdministratorAsync();
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, administrator);
        var (antiforgeryCookie, antiforgeryToken) = await AdministrationTestFlow.AntiforgeryAsync(factory, administrator);
        var correlationId = NextCorrelationId();

        using var response = await AdministrationTestFlow.PostAsync(
            client,
            "/Admin/Roles/Revoke",
            new Dictionary<string, string>
            {
                ["id"] = administrator.Id,
                ["roleName"] = RoleNames.Administrator,
            },
            cookie,
            antiforgeryCookie,
            antiforgeryToken,
            correlationId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Contains(RoleNames.Administrator, await userManager.GetRolesAsync(administrator));

        var audit = Assert.Single(SsoTestFlow.AuditEvents(
            factory, AuditActions.AuthorizationRoleRevoked, correlationId));
        Assert.True(SsoTestFlow.AuditEquals(audit, "Denied"));
    }

    /// <summary>
    /// ADR-0017 §17: revoking the Administrator role from the last effective
    /// Administrator is refused. The actor here holds the canonical
    /// <c>Roles.Assign</c> permission without being an Administrator, which is
    /// exactly the arrangement in which §17 is the only remaining guard.
    /// </summary>
    [Fact]
    public async Task The_last_effective_administrator_cannot_be_revoked()
    {
        var target = await AdministrationTestFlow.CreateUserAsync(
            factory, "تنها مدیر", LocalUserKind.Administrator, RoleNames.Administrator);
        await AdministrationTestFlow.DemoteAdministratorsExceptAsync(factory, target);

        var actor = await AdministrationTestFlow.CreateCapableNonAdministratorAsync(factory, "کاربر دارای مجوز");
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, actor);
        var (antiforgeryCookie, antiforgeryToken) = await AdministrationTestFlow.AntiforgeryAsync(factory, actor);
        var correlationId = NextCorrelationId();

        using var response = await AdministrationTestFlow.PostAsync(
            client,
            "/Admin/Roles/Revoke",
            new Dictionary<string, string>
            {
                ["id"] = target.Id,
                ["roleName"] = RoleNames.Administrator,
            },
            cookie,
            antiforgeryCookie,
            antiforgeryToken,
            correlationId);

        var page = await AdministrationTestFlow.FollowAsync(client, response, cookie);
        Assert.Contains(OnPage("امکان حذف آخرین مدیر سامانه وجود ندارد"), page, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Contains(RoleNames.Administrator, await userManager.GetRolesAsync(target));

        var audit = Assert.Single(SsoTestFlow.AuditEvents(
            factory, AuditActions.AuthorizationRoleRevoked, correlationId));
        Assert.True(SsoTestFlow.AuditEquals(audit, "Denied"));
    }

    /// <summary>ADR-0004 §8.2 + ADR-0017 §8: the administration screen shows only a masked National ID.</summary>
    [Fact]
    public async Task The_user_screens_never_reveal_a_full_national_id()
    {
        var target = await AdministrationTestFlow.CreateUserAsync(factory, "کاربر هدف", LocalUserKind.Staff);
        var administrator = await CreateAdministratorAsync();
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, administrator);

        var masked = target.NationalId[..3] + "****" + target.NationalId[^2..];

        // The list is searched by name so the target row is the one under test
        // regardless of how many users earlier tests created — and so the page
        // never echoes the National ID back in the search box either.
        foreach (var path in new[]
                 {
                     $"/Admin/Users?q={Uri.EscapeDataString(target.FullName)}",
                     $"/Admin/Users/Details?id={target.Id}",
                 })
        {
            var page = await ReadAsync(client, path, cookie);
            Assert.Contains(masked, page, StringComparison.Ordinal);
            Assert.DoesNotContain(target.NationalId, page, StringComparison.Ordinal);
        }
    }

    /// <summary>ADR-0017 §6/§10: the mapping is displayed, never edited.</summary>
    [Fact]
    public async Task The_role_permissions_page_is_read_only_and_shows_the_canonical_catalog()
    {
        var administrator = await CreateAdministratorAsync();
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, administrator);

        var page = await ReadAsync(client, "/Admin/Roles", cookie);

        Assert.Contains("Users.Manage", page, StringComparison.Ordinal);
        Assert.Contains("Roles.Assign", page, StringComparison.Ordinal);
        Assert.Contains(OnPage("بدون دسترسی ویژه"), page, StringComparison.Ordinal);
        // Literal Persian text in a Razor view is written verbatim (only @...
        // expressions are HTML-encoded), so this assertion needs no encoding —
        // unlike the table cell below, which renders an expression.
        Assert.Contains("قابل ویرایش نیستند", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.Manage", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Students.View", page, StringComparison.Ordinal);
    }

    /// <summary>AGENTS §5: a validation failure re-renders the form with the Persian message.</summary>
    [Fact]
    public async Task Creating_a_duplicate_national_id_re_renders_the_form_with_a_persian_message()
    {
        var existing = await SsoTestFlow.CreateUserAsync(
            factory, SsoTestFlow.NextNationalId(), "کاربر موجود", LocalUserKind.Staff);
        var administrator = await CreateAdministratorAsync();
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, administrator);
        var (antiforgeryCookie, antiforgeryToken) = await AdministrationTestFlow.AntiforgeryAsync(factory, administrator);

        using var response = await AdministrationTestFlow.PostAsync(
            client,
            "/Admin/Users/Create",
            new Dictionary<string, string>
            {
                ["NationalId"] = existing.NationalId,
                ["FullName"] = "تکراری",
                ["MobileNumber"] = "09123456789",
                ["Kind"] = nameof(LocalUserKind.Staff),
            },
            cookie,
            antiforgeryCookie,
            antiforgeryToken,
            correlationId: NextCorrelationId());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(OnPage("کاربری با این شناسه ملی قبلاً ثبت شده است."), body, StringComparison.Ordinal);
        Assert.Contains(OnPage("تکراری"), body, StringComparison.Ordinal); // entered value is preserved
    }

    /// <summary>AGENTS §18 (over-posting): only the explicit view-model fields may bind.</summary>
    [Fact]
    public async Task Extra_posted_fields_cannot_alter_protected_account_properties()
    {
        var other = await AdministrationTestFlow.CreateUserAsync(
            factory, "کاربر دیگر", LocalUserKind.Staff, RoleNames.GraduateStaff);
        var administrator = await CreateAdministratorAsync();
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, administrator);
        var (antiforgeryCookie, antiforgeryToken) =
            await AdministrationTestFlow.AntiforgeryAsync(factory, administrator);
        var nationalId = SsoTestFlow.NextNationalId();

        using var response = await AdministrationTestFlow.PostAsync(
            client,
            "/Admin/Users/Create",
            new Dictionary<string, string>
            {
                ["NationalId"] = nationalId,
                ["FullName"] = "کاربر تازه",
                ["MobileNumber"] = "09123456789",
                ["Kind"] = nameof(LocalUserKind.Staff),
                ["Roles"] = RoleNames.GraduateStaff,

                // No view model carries these: they must be ignored entirely.
                ["Id"] = other.Id,
                ["PasswordHash"] = "attacker-supplied-hash",
                ["SecurityStamp"] = "attacker-supplied-stamp",
                ["IsAdmin"] = "true",
                ["LockoutEnabled"] = "false",
            },
            cookie,
            antiforgeryCookie,
            antiforgeryToken,
            NextCorrelationId());

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var created = await userManager.FindByNameAsync(nationalId);
        Assert.NotNull(created);
        Assert.NotEqual(other.Id, created!.Id);
        Assert.Null(created.PasswordHash);
        Assert.NotEqual("attacker-supplied-stamp", created.SecurityStamp);
        Assert.True(created.LockoutEnabled);
        Assert.Equal(LocalUserKind.Staff, created.UserKind);

        var roles = await userManager.GetRolesAsync(created);
        Assert.Equal(new[] { RoleNames.GraduateStaff }, roles); // IsAdmin=true granted nothing
    }

    /// <summary>AGENTS §22/§18 (IDOR): an unknown id answers 404 — never 200, never a leak.</summary>
    [Fact]
    public async Task Details_for_an_unknown_user_id_returns_not_found()
    {
        var administrator = await CreateAdministratorAsync();
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, administrator);

        using var response = await AdministrationTestFlow.GetAsync(
            client,
            $"/Admin/Users/Details?id={Guid.NewGuid().ToString("N")}",
            cookie);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// AGENTS §16: search folds Arabic Yeh, Persian digits and repeated
    /// whitespace on both sides, while the stored display value keeps its
    /// original characters (the row still renders as it was stored).
    /// </summary>
    [Fact]
    public async Task User_search_folds_Arabic_variants_digits_and_whitespace()
    {
        var suffix = SsoTestFlow.NextNationalId();
        var storedName = "محمدي " + ToPersianDigits(suffix); // Arabic Yeh + Persian digits + one space
        var target = await AdministrationTestFlow.CreateUserAsync(factory, storedName, LocalUserKind.Staff);
        var administrator = await CreateAdministratorAsync();
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, administrator);

        var rawQuery = "  محمدی   " + suffix; // Persian Yeh + ASCII digits + extra spaces
        Assert.Equal(
            PersianTextNormalizer.NormalizeSearchTerm(storedName),
            PersianTextNormalizer.NormalizeSearchTerm(rawQuery));

        var page = await ReadAsync(
            client, $"/Admin/Users?q={Uri.EscapeDataString(rawQuery)}", cookie);

        Assert.Contains(IdentityDisplayMask.NationalId(target.NationalId), page, StringComparison.Ordinal);

        // The row renders the stored value verbatim — Razor HTML-encodes it
        // (same reason as OnPage elsewhere in this class).
        Assert.Contains(OnPage(storedName), page, StringComparison.Ordinal);
    }

    /// <summary>Display-only conversion used to build a stored value in the Arabic/Persian mixed form.</summary>
    private static string ToPersianDigits(string value) =>
        string.Concat(value.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c));

    private async Task<ApplicationUser> CreateAdministratorAsync() =>
        await AdministrationTestFlow.CreateUserAsync(
            factory, "مدیر سامانه", LocalUserKind.Administrator, RoleNames.Administrator);

    private static async Task<string> ReadAsync(HttpClient client, string path, string cookie)
    {
        using var response = await AdministrationTestFlow.GetAsync(client, path, cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>
    /// Razor renders through <c>HtmlEncoder.Default</c>, which emits Persian as
    /// numeric character references — the same reason <c>LocalLoginTests.OnPage</c>
    /// encodes its expected message. ASCII identifiers are unaffected.
    /// </summary>
    private static string OnPage(string message) =>
        System.Text.Encodings.Web.HtmlEncoder.Default.Encode(message);

    private static string NextCorrelationId() => "test-" + Guid.NewGuid().ToString("N");
}
