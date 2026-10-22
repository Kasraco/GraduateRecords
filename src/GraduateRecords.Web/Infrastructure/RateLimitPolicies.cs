namespace GraduateRecords.Web.Infrastructure;

/// <summary>Named rate-limiter policies (AGENTS §18: login endpoints are rate limited).</summary>
public static class RateLimitPolicies
{
    public const string Login = "login";
}
