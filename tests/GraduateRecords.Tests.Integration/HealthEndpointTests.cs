using System.Net;
using System.Text.Json;

namespace GraduateRecords.Tests.Integration;

[Collection(WebApplicationCollection.Name)]
public sealed class HealthEndpointTests
{
    private readonly HttpClient _client;

    public HealthEndpointTests(GraduateRecordsWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Live_returns_healthy_without_sensitive_detail()
    {
        var response = await _client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"Healthy\"", body, StringComparison.Ordinal);
        AssertDoesNotLeakSecrets(body);
    }

    [Fact]
    public async Task Ready_returns_status_only_without_sensitive_detail()
    {
        var response = await _client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);

        using var document = JsonDocument.Parse(body);
        Assert.True(document.RootElement.TryGetProperty("status", out var status));
        Assert.Equal("Unhealthy", status.GetString());
        Assert.Single(document.RootElement.EnumerateObject());
        AssertDoesNotLeakSecrets(body);
    }

    private static void AssertDoesNotLeakSecrets(string body)
    {
        Assert.DoesNotContain("ConnectionString", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RootPath", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Server=", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".data", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
    }
}
