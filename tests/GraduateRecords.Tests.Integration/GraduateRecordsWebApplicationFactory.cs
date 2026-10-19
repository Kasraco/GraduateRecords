using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;

namespace GraduateRecords.Tests.Integration;

public sealed class GraduateRecordsWebApplicationFactory : WebApplicationFactory<Program>
{
    public CapturingLogSink Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services => services.AddSingleton<ILogEventSink>(Logs));

        builder.ConfigureAppConfiguration((_, config) =>
        {
            var fileRoot = Path.Combine(Path.GetTempPath(), "GraduateRecordsTests", "files");
            Directory.CreateDirectory(fileRoot);

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FileStorage:RootPath"] = fileRoot,
                // Intentional non-routable host so readiness fails closed without needing LocalDB in CI.
                ["ConnectionStrings:Default"] =
                    "Server=127.0.0.1,65530;Database=GraduateRecords_Tests;User ID=sa;Password=unused;TrustServerCertificate=True;Encrypt=False;Connect Timeout=1;Command Timeout=1",
            });
        });
    }
}

public sealed class CapturingLogSink : ILogEventSink
{
    private readonly List<LogEvent> _events = [];
    private readonly object _gate = new();

    public void Emit(LogEvent logEvent)
    {
        lock (_gate)
        {
            _events.Add(logEvent);
        }
    }

    public IReadOnlyList<LogEvent> Snapshot()
    {
        lock (_gate)
        {
            return _events.ToList();
        }
    }
}
