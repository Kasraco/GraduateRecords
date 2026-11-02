using System.Net;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// UC3 authorization matrix (AGENTS §22 "Web / authorization matrix ... for each
/// endpoint taking an id"): anonymous / student / staff-without-permission /
/// administrator, plus the global antiforgery rule. Enforcement comes from the
/// explicit <c>Permission:*</c> policies (ADR-0004 §10.2, ADR-0017 §21) — no
/// endpoint is reachable by UI visibility alone, and neither <c>UserKind</c> nor
/// the authentication path grants anything (ADR-0017 §1/§4).
/// </summary>
[Collection(WebApplicationCollection.Name)]
public sealed class AdministrationAuthorizationTests(GraduateRecordsWebApplicationFactory factory)
{
    private static readonly string[] GetPaths =
    [
        "/Admin/Users",
        "/Admin/Users/Create",
        "/Admin/Roles",
        "/Admin/Users/Details?id=does-not-matter",
    ];

    private static readonly string[] PostPaths =
    [
        "/Admin/Users/Create",
        "/Admin/Roles/Grant",
        "/Admin/Roles/Revoke",
    ];

    [Theory]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/Users/Create")]
    [InlineData("/Admin/Roles")]
    [InlineData("/Admin/Users/Details?id=does-not-matter")]
    public async Task Anonymous_requests_are_unauthorized(string path)
    {
        using var client = SsoTestFlow.CreateClient(factory);
        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/Users/Create")]
    [InlineData("/Admin/Roles")]
    [InlineData("/Admin/Users/Details?id=does-not-matter")]
    public async Task Anonymous_posts_are_unauthorized(string path)
    {
        using var client = SsoTestFlow.CreateClient(factory);
        using var content = new FormUrlEncodedContent([new KeyValuePair<string, string>("id", "x")]);
        using var response = await client.PostAsync(path, content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/Users/Create")]
    [InlineData("/Admin/Roles")]
    [InlineData("/Admin/Users/Details?id=does-not-matter")]
    public async Task A_student_role_holder_gets_forbidden(string path)
    {
        var user = await AdministrationTestFlow.CreateUserAsync(
            factory, "دانشجوی آزمایشی", LocalUserKind.Student, RoleNames.Student);
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, user);

        using var response = await AdministrationTestFlow.GetAsync(client, path, cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("/Admin/Users")]
    [InlineData("/Admin/Users/Create")]
    [InlineData("/Admin/Roles")]
    [InlineData("/Admin/Users/Details?id=does-not-matter")]
    public async Task A_staff_user_without_the_capability_gets_forbidden(string path)
    {
        // GraduateStaff carries no Phase 1A feature permission (ADR-0017 §8).
        var user = await AdministrationTestFlow.CreateUserAsync(
            factory, "کارمند آزمایشی", LocalUserKind.Staff, RoleNames.GraduateStaff);
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, user);

        using var response = await AdministrationTestFlow.GetAsync(client, path, cookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_administrator_can_open_every_screen()
    {
        var target = await AdministrationTestFlow.CreateUserAsync(
            factory, "کاربر هدف", LocalUserKind.Staff);
        var administrator = await AdministrationTestFlow.CreateUserAsync(
            factory, "مدیر سامانه", LocalUserKind.Administrator, RoleNames.Administrator);
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, administrator);

        foreach (var path in new[]
                 {
                     "/Admin/Users",
                     "/Admin/Users/Create",
                     "/Admin/Roles",
                     $"/Admin/Users/Details?id={target.Id}",
                 })
        {
            using var response = await AdministrationTestFlow.GetAsync(client, path, cookie);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    [Fact]
    public async Task A_student_cannot_grant_a_role_even_with_a_valid_antiforgery_token()
    {
        var target = await AdministrationTestFlow.CreateUserAsync(
            factory, "کاربر هدف", LocalUserKind.Staff);
        var student = await AdministrationTestFlow.CreateUserAsync(
            factory, "دانشجوی مهاجم", LocalUserKind.Student, RoleNames.Student);
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, student);
        var (cookieToken, requestToken) = await AdministrationTestFlow.AntiforgeryAsync(factory, student);

        using var response = await AdministrationTestFlow.PostAsync(
            client,
            "/Admin/Roles/Grant",
            new Dictionary<string, string>
            {
                ["id"] = target.Id,
                ["roleName"] = RoleNames.Administrator,
            },
            cookie,
            cookieToken,
            requestToken,
            correlationId: "test-" + Guid.NewGuid().ToString("N"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // Fail-closed: the target gained nothing.
        using var scope = factory.Services.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.False(await userManager.IsInRoleAsync(target, RoleNames.Administrator));
    }

    [Fact]
    public async Task An_authenticated_post_without_an_antiforgery_token_is_rejected()
    {
        var administrator = await AdministrationTestFlow.CreateUserAsync(
            factory, "مدیر سامانه", LocalUserKind.Administrator, RoleNames.Administrator);
        using var client = SsoTestFlow.CreateClient(factory);
        var cookie = await AdministrationTestFlow.SignInAsync(factory, client, administrator);

        // Session cookie only: no antiforgery cookie and no CSRF header.
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/Admin/Roles/Grant?roleName=x");
        request.Headers.Add("Cookie", "GraduateRecords.Auth=" + cookie);
        request.Content = new FormUrlEncodedContent(
            [new KeyValuePair<string, string>("id", "someone")]);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
