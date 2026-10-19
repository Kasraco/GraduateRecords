using System.Net;

namespace GraduateRecords.Tests.Integration;

[Collection(WebApplicationCollection.Name)]
public sealed class ExceptionHandlerTests
{
    private readonly HttpClient _client;

    public ExceptionHandlerTests(GraduateRecordsWebApplicationFactory factory)
    {
        _client = factory.CreateClient(new()
        {
            AllowAutoRedirect = false,
        });
    }

    [Fact]
    public async Task Unhandled_exception_returns_exact_Persian_text_and_correlation_id_without_leakage()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/__test/throw");
        request.Headers.Add("X-Correlation-Id", "phase0-test-correlation");

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Contains("خطایی رخ داده است.", body, StringComparison.Ordinal);
        Assert.Contains("کد پیگیری:", body, StringComparison.Ordinal);
        Assert.Contains("phase0-test-correlation", body, StringComparison.Ordinal);
        Assert.DoesNotContain("test-exception", body, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", body, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
    }
}
