using GraduateRecords.Infrastructure.Health;
using GraduateRecords.Infrastructure.Options;
using GraduateRecords.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using GraduateRecords.Infrastructure.Logging;
using Serilog.Core;

namespace GraduateRecords.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string environmentName,
        string webRootPath)
    {
        services
            .AddOptions<FileStorageOptions>()
            .Bind(configuration.GetSection(FileStorageOptions.SectionName))
            .ValidateDataAnnotations()
            .Validate(
                options => FileStorageOptions.IsSafeRoot(options.RootPath, webRootPath),
                "FileStorage:RootPath must be fully qualified and outside the public web root.")
            .ValidateOnStart();

        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection("ConnectionStrings"))
            .Validate(options => DatabaseOptions.IsValid(options.Default, environmentName),
                "ConnectionStrings:Default is missing, invalid, or violates database TLS requirements.")
            .ValidateOnStart();

        services.AddDbContext<AppDbContext>((provider, options) =>
            options.UseSqlServer(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.Default,
                sql => sql.UseCompatibilityLevel(170)));

        services.AddSingleton<ILogEventSink>(
            new DatabaseLogSink(configuration.GetConnectionString("Default") ?? string.Empty));

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
            .AddCheck<FileStorageHealthCheck>("filestorage", tags: ["ready"])
            .AddCheck("self", () => Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy(), tags: ["live"]);

        return services;
    }
}
