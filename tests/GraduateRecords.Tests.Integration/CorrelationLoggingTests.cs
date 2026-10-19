using System.Net;

namespace GraduateRecords.Tests.Integration;

[Collection(WebApplicationCollection.Name)]
public sealed class CorrelationLoggingTests(GraduateRecordsWebApplicationFactory factory)
{
    [Fact]
    public async Task Correlation_id_is_present_in_request_completion_logs()
    {
        var correlationId = Guid.NewGuid().ToString("N");
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", correlationId);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("X-Correlation-Id"));

        var completion = factory.Logs.Snapshot()
            .Where(e => e.MessageTemplate.Text.Contains("HTTP request completed", StringComparison.Ordinal))
            .Where(e => e.Properties.TryGetValue("CorrelationId", out var value)
                        && value.ToString().Contains(correlationId, StringComparison.Ordinal))
            .ToList();

        Assert.True(completion.Count > 0,
            "Request-completion log did not carry the CorrelationId property (AGENTS §19).");
    }
}
