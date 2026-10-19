using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace GraduateRecords.Tests.Integration;

[Collection(WebApplicationCollection.Name)]
public sealed class AuthenticationTests(GraduateRecordsWebApplicationFactory factory)
{
    [Theory]
    [InlineData("/")]
    [InlineData("/Home/Index")]
    public async Task Anonymous_requests_cannot_access_application_endpoints(string path)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public void Authentication_and_antiforgery_are_global_defaults()
    {
        var mvc = factory.Services.GetRequiredService<IOptions<MvcOptions>>().Value;
        Assert.Contains(mvc.Filters, f => f is AuthorizeFilter);
        Assert.Contains(mvc.Filters, f => f is AutoValidateAntiforgeryTokenAttribute);

        var authorization = factory.Services.GetRequiredService<IOptions<AuthorizationOptions>>().Value;
        Assert.NotNull(authorization.FallbackPolicy);
        Assert.Contains(authorization.FallbackPolicy.Requirements,
            r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
    }

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Health_endpoints_remain_anonymous(string path)
    {
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false });
        var response = await client.GetAsync(path);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
