using NetArchTest.Rules;

namespace GraduateRecords.Tests.Architecture;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Domain_must_not_depend_on_Application_Infrastructure_Web_or_framework_ui()
    {
        var result = Types.InAssembly(typeof(Domain.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "GraduateRecords.Application",
                "GraduateRecords.Infrastructure",
                "GraduateRecords.Web",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions.Logging")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailures(result));
    }

    [Fact]
    public void Application_must_not_depend_on_Infrastructure_Web_or_SqlServer()
    {
        var result = Types.InAssembly(typeof(Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "GraduateRecords.Infrastructure",
                "GraduateRecords.Web",
                "Microsoft.EntityFrameworkCore.SqlServer",
                "Microsoft.Data.SqlServer",
                "Microsoft.AspNetCore.Mvc",
                "Microsoft.AspNetCore.Http")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailures(result));
    }

    [Fact]
    public void Infrastructure_must_not_depend_on_Web()
    {
        var result = Types.InAssembly(typeof(Infrastructure.DependencyInjection).Assembly)
            .ShouldNot()
            .HaveDependencyOn("GraduateRecords.Web")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailures(result));
    }

    [Fact]
    public void Controllers_must_not_use_AppDbContext()
    {
        var controllerAssembly = typeof(Web.Controllers.HomeController).Assembly;
        var result = Types.InAssembly(controllerAssembly)
            .That()
            .ResideInNamespace("GraduateRecords.Web.Controllers")
            .Or()
            .ResideInNamespaceStartingWith("GraduateRecords.Web.Areas")
            .ShouldNot()
            .HaveDependencyOn("GraduateRecords.Infrastructure.Persistence")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailures(result));
    }

    private static string FormatFailures(TestResult result)
    {
        if (result.FailingTypes is null || result.FailingTypes.Count == 0)
        {
            return "Architecture rule failed with no failing type list.";
        }

        return "Violations: " + string.Join(", ", result.FailingTypes.Select(static t => t.FullName));
    }
}
