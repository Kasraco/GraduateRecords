using GraduateRecords.Infrastructure.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace GraduateRecords.Infrastructure.Health;

public sealed class FileStorageHealthCheck(IOptions<FileStorageOptions> options) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var rootPath = options.Value.RootPath;
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return Task.FromResult(HealthCheckResult.Unhealthy());
            }

            Directory.CreateDirectory(rootPath);

            var probePath = Path.Combine(rootPath, $".health-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probePath, "ok");
            File.Delete(probePath);

            return Task.FromResult(HealthCheckResult.Healthy());
        }
        catch (Exception)
        {
            // Never surface paths or exception text in health payloads.
            return Task.FromResult(HealthCheckResult.Unhealthy());
        }
    }
}
