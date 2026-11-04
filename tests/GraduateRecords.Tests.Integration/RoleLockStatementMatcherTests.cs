namespace GraduateRecords.Tests.Integration;

/// <summary>
/// C2: the production Administrator role-row locking statement must be
/// distinguishable from the pre-lock <c>RoleManager.FindByNameAsync</c> lookup.
/// Both statements name <c>AspNetRoles</c>, so the text check has to rest on
/// the lock hints (present only in the production statement) plus the absence
/// of the lookup's discriminating column. Narrowly scoped by design: two
/// positive markers and one negative marker, ordinal-ignore-case, no other
/// normalization of SQL text — these tests pin exactly that contract.
/// </summary>
public sealed class RoleLockStatementMatcherTests
{
    private const string ProductionLockStatement =
        "(@__administrator_Id_0 nvarchar(450))SELECT * FROM [AspNetRoles] WITH (UPDLOCK, HOLDLOCK) " +
        "WHERE [Id] = @__administrator_Id_0";

    private const string PreLockRoleLookup =
        "SELECT [r].[Id], [r].[ConcurrencyStamp], [r].[Name], [r].[NormalizedName] " +
        "FROM [AspNetRoles] AS [r] WHERE [r].[NormalizedName] = @__normalizedName_0";

    [Fact]
    public void The_production_administrator_lock_statement_is_matched() =>
        Assert.True(BootstrapEvidence.IsAdministratorRoleLockStatement(ProductionLockStatement));

    [Fact]
    public void The_pre_lock_role_lookup_is_rejected() =>
        // Contains AspNetRoles, but no lock hints — and the NormalizedName
        // marker is mandatory-negative so the lookup can never match.
        Assert.False(BootstrapEvidence.IsAdministratorRoleLockStatement(PreLockRoleLookup));

    [Fact]
    public void Statements_missing_either_lock_hint_or_the_role_table_are_rejected()
    {
        Assert.False(BootstrapEvidence.IsAdministratorRoleLockStatement(
            "SELECT 1 FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK, INDEX(IX_AspNetUsers_NationalId)) WHERE [NationalId] = @nationalId"));
        Assert.False(BootstrapEvidence.IsAdministratorRoleLockStatement(
            "SELECT * FROM [AspNetRoles] WITH (UPDLOCK) WHERE [Id] = @__administrator_Id_0"));
        Assert.False(BootstrapEvidence.IsAdministratorRoleLockStatement(
            "INSERT INTO [AspNetUsers] ([Id], [NationalId]) VALUES (@p0, @p1)"));
        Assert.False(BootstrapEvidence.IsAdministratorRoleLockStatement(null));
    }

    [Fact]
    public void Marker_matching_is_ordinal_ignore_case_only_and_never_normalizes_further()
    {
        // Case-insensitivity of the markers, without any whitespace/comma
        // rewriting that could make an unrelated statement match.
        Assert.True(BootstrapEvidence.IsAdministratorRoleLockStatement(
            "select * from [aspnetroles] with (updlock, holdlock) where [Id] = @id"));
        Assert.False(BootstrapEvidence.IsAdministratorRoleLockStatement(
            "select * from [aspnetroles] where [Id] = @id /* UPDLOCK HOLDLOCK */"));
    }
}
