namespace GraduateRecords.Web.Infrastructure;

internal static class DevelopmentConfiguration
{
    /// <summary>
    /// AGENTS §13 "Development SSO / Authentication Rule": the ONLY identity the
    /// development-only sign-in endpoint accepts. It is an explicit local test
    /// principal — never a National ID, never a university credential, and never
    /// accepted in any environment other than Development (the endpoint is not
    /// registered elsewhere). Keyed lookup value for <c>/dev/login/{identityKey}</c>.
    /// </summary>
    public const string TestIdentityKey = "dev-test";

    /// <summary>User name of the controlled development test identity.</summary>
    public const string TestIdentityUserName = "dev-test";

    /// <summary>Display/audit name of the controlled development test identity.</summary>
    public const string TestIdentityFullName = "کاربر آزمایشی توسعه";

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
