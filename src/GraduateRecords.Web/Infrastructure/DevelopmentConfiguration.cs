namespace GraduateRecords.Web.Infrastructure;

internal static class DevelopmentConfiguration
{
    public static void EnsureFileStorageRoot(WebApplicationBuilder builder)
    {
        if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(builder.Configuration["FileStorage:RootPath"]))
        {
            return;
        }

        // ContentRoot is src/GraduateRecords.Web → repo root is two levels up.
        var repoRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", ".."));
        var rootPath = Path.Combine(repoRoot, ".data", "files");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FileStorage:RootPath"] = rootPath,
        });
    }
}
