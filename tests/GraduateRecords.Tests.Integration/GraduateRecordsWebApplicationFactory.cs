using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace GraduateRecords.Tests.Integration;

public sealed class GraduateRecordsWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

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
