using NetArchTest.Rules;

namespace GraduateRecords.Tests.Architecture;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Domain_must_not_depend_on_any_outer_layer_or_framework()
    {
        var result = Types.InAssembly(typeof(Domain.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "GraduateRecords.Application",
                "GraduateRecords.Infrastructure",
                "GraduateRecords.Web",
                "Microsoft.EntityFrameworkCore",
                "Microsoft.AspNetCore",
                "Microsoft.Extensions.Logging",
                "System.Net.Http",
                "System.IO")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailures(result));
    }

    [Fact]
    public void Application_must_not_depend_on_Infrastructure_Web_Identity_or_SqlServer()
    {
        var result = Types.InAssembly(typeof(Application.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "GraduateRecords.Infrastructure",
                "GraduateRecords.Web",
                "Microsoft.EntityFrameworkCore.SqlServer",
                "Microsoft.EntityFrameworkCore.Relational",
                "Microsoft.Data.SqlClient",
                "Microsoft.AspNetCore.Mvc",
                "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Identity",
                "System.Net.Http",
                "System.IO")
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
    public void Web_controllers_must_not_depend_on_Infrastructure()
    {
        var result = Types.InAssembly(typeof(Web.Controllers.HomeController).Assembly)
            .That()
            .ResideInNamespace("GraduateRecords.Web.Controllers")
            .Or()
            .ResideInNamespaceStartingWith("GraduateRecords.Web.Areas")
            .ShouldNot()
            .HaveDependencyOn("GraduateRecords.Infrastructure")
            .GetResult();

        Assert.True(result.IsSuccessful, FormatFailures(result));
    }

    [Fact]
    public void Web_to_Infrastructure_is_limited_to_the_composition_root()
    {
        // AGENTS §2.2 permits Web → Infrastructure only for Program.cs composition and,
        // later, ASP.NET Identity plumbing inside AccountController. AccountController
        // does not exist yet.
        var result = Types.InAssembly(typeof(Web.Controllers.HomeController).Assembly)
            .That()
            .ResideInNamespaceStartingWith("GraduateRecords.Web")
            .And()
            .DoNotHaveNameStartingWith("Program")
            .And()
            .DoNotHaveName("AccountController")
            .ShouldNot()
            .HaveDependencyOn("GraduateRecords.Infrastructure")
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
