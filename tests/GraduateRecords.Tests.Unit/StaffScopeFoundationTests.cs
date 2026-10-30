using GraduateRecords.Application.Common.Security;

namespace GraduateRecords.Tests.Unit;

/// <summary>
/// UC2.1 StaffScope foundation (ADR-0017 StaffScope rule): the exact Phase 1
/// enum values and the ordinal, fail-closed role → scope derivation.
/// Comparison/resource-access APIs (WithinScope/CanAccess) are deferred to
/// UC2.5 and are deliberately not exercised here.
/// </summary>
public sealed class StaffScopeFoundationTests
{
    [Fact]
    public void StaffScope_contains_exactly_None_and_AllStudents()
    {
        string[] expected = ["AllStudents", "None"];
        string[] actual = Enum.GetNames<StaffScope>()
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expected, actual);
    }

    [Theory]
    // Complete FromRoles matrix — scope derives from role names only:
    [InlineData(new[] { "Student" }, StaffScope.None)]
    [InlineData(new[] { "GraduateStaff" }, StaffScope.AllStudents)]
    [InlineData(new[] { "Administrator" }, StaffScope.None)]
    [InlineData(new[] { "Administrator", "GraduateStaff" }, StaffScope.AllStudents)]
    [InlineData(new[] { "GraduateStaff", "Administrator" }, StaffScope.AllStudents)]
    [InlineData(new[] { "Student", "GraduateStaff" }, StaffScope.AllStudents)]
    [InlineData(new[] { "Student", "Administrator" }, StaffScope.None)]
    // Administrator + GraduateStaff => AllStudents only because of GraduateStaff,
    // never because Administrator implicitly grants student-data access:
    [InlineData(new[] { "Administrator", "GraduateStaff", "Student" }, StaffScope.AllStudents)]
    // Empty and unknown inputs fail closed to None:
    [InlineData(new string[] { }, StaffScope.None)]
    [InlineData(new[] { "unknown" }, StaffScope.None)]
    [InlineData(new[] { "Student", "unknown" }, StaffScope.None)]
    // Ordinal matching — non-canonical casing fails closed to None:
    [InlineData(new[] { "graduatestaff" }, StaffScope.None)]
    [InlineData(new[] { "GRADUATESTAFF" }, StaffScope.None)]
    public void FromRoles_derives_scope_from_role_names_only(string[] roleNames, StaffScope expected) =>
        Assert.Equal(expected, ScopeExtensions.FromRoles(roleNames));
}
