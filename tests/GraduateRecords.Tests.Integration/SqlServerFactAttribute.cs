using Xunit;

namespace GraduateRecords.Tests.Integration;

/// <summary>
/// xunit v2 has no runtime skip, so the environment gate is applied at
/// <b>discovery</b> time: when <see cref="ConnectionStringVariable"/> is absent
/// the test is reported as <c>Skipped</c> (never <c>Passed</c>), and nothing is
/// executed. Setting the variable makes the same test run against a real SQL
/// Server. No package or project was added for this (AGENTS §20).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class SqlServerFactAttribute : FactAttribute
{
    public const string ConnectionStringVariable = "GRADUATERECORDS_TEST_SQLSERVER";

    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionStringVariable)))
        {
            Skip =
                $"SQL Server unavailable: set the {ConnectionStringVariable} environment variable to run this test.";
        }
    }
}
