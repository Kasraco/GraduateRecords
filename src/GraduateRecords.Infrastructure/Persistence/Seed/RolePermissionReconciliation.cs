using System.Security.Claims;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace GraduateRecords.Infrastructure.Persistence.Seed;

/// <summary>
/// UC2.3 — idempotent canonical role and permission-claim reconciliation
/// (AGENTS §17 "seeds … are idempotent"; ADR-0017 §7–§8: the Application-layer
/// catalog is the single code authority and the Identity role claims are its
/// persisted projection; ADR-0017 §21 fail-closed).
///
/// Runs once per application start from the composition root
/// (<c>Program.cs</c>), before the application serves requests and before the
/// future ADR-0016 Bootstrap Administrator step (bootstrap assigns the
/// Administrator role, so the canonical catalog must exist first). For each of
/// the three canonical roles it converges that role's <c>permission</c> claims
/// to <see cref="RolePermissionCatalog"/> exactly: add missing values, remove
/// values the catalog no longer declares, and retain exactly one occurrence of
/// each expected value — while preserving every non-permission claim.
///
/// Never touches users, user-role memberships, non-canonical roles or claims
/// outside <see cref="PermissionClaims.Type"/>. Contains no UserKind,
/// authentication-method or bootstrap-configuration input (ADR-0017 §1/§4).
/// </summary>
public sealed class RolePermissionReconciliation(
    RoleManager<ApplicationRole> roleManager,
    ILogger<RolePermissionReconciliation> logger)
{
    /// <summary>Deterministic processing order (ADR-0017 §2 canonical names).</summary>
    private static readonly string[] CanonicalRoles =
        [RoleNames.Student, RoleNames.GraduateStaff, RoleNames.Administrator];

    /// <summary>
    /// Converges the canonical roles and their permission claims to the
    /// Application catalog. Idempotent: a converged state produces zero writes.
    /// Any Identity failure is logged with its context and rethrown so startup
    /// aborts — the application never serves with an unverified authorization
    /// catalog. Cancellation is checked between roles and before each write
    /// phase and propagates untouched.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var createdRoles = 0;
        var addedClaims = 0;
        var removedClaims = 0;
        var unchangedRoles = 0;

        foreach (var roleName in CanonicalRoles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (role, created) = await EnsureRoleAsync(roleName, cancellationToken);
            if (created)
            {
                createdRoles++;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var expected = RolePermissionCatalog.GetPermissions(roleName);
            var before = (await roleManager.GetClaimsAsync(role)).ToList();
            var removalDelta = ComputePermissionClaimDelta(before, expected);

            if (removalDelta.ToAdd.Count == 0 && removalDelta.ToRemove.Count == 0)
            {
                unchangedRoles++;
                continue;
            }

            // Phase 1 — removals from the pre-change snapshot: stale values
            // (not in the catalog) and every duplicate occurrence of an
            // expected value beyond the first.
            foreach (var claim in removalDelta.ToRemove)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await roleManager.RemoveClaimAsync(role, claim);
                if (!result.Succeeded)
                {
                    throw IdentityFailure("remove permission claim", roleName, claim.Value, result);
                }

                removedClaims++;
                logger.LogInformation(
                    "Reconciliation removed permission claim {PermissionValue} from role {RoleName} (Result: {Result}).",
                    claim.Value,
                    roleName,
                    nameof(IdentityResult.Success));
            }

            // Phase 2 — additions from a FRESH claim snapshot. Re-reading after
            // the removals makes the outcome independent of how each store
            // handles duplicate rows: EF Core's RoleStore removes one matching
            // row per RemoveClaimAsync call (duplicates are removed by the
            // repeated calls above), while the in-memory test store removes
            // every matching row on the first call — either way this phase
            // re-adds any expected value that is now missing, so both stores
            // converge to exactly one occurrence of each catalog value.
            cancellationToken.ThrowIfCancellationRequested();

            var after = (await roleManager.GetClaimsAsync(role)).ToList();
            foreach (var claim in ComputePermissionClaimDelta(after, expected).ToAdd)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await roleManager.AddClaimAsync(role, claim);
                if (!result.Succeeded)
                {
                    throw IdentityFailure("add permission claim", roleName, claim.Value, result);
                }

                addedClaims++;
                logger.LogInformation(
                    "Reconciliation added permission claim {PermissionValue} to role {RoleName} (Result: {Result}).",
                    claim.Value,
                    roleName,
                    nameof(IdentityResult.Success));
            }
        }

        logger.LogInformation(
            "Role/permission reconciliation completed: created roles {CreatedRoles}, added permission claims {AddedClaims}, removed permission claims {RemovedClaims}, unchanged roles {UnchangedRoles}.",
            createdRoles,
            addedClaims,
            removedClaims,
            unchangedRoles);
    }

    /// <summary>
    /// Pure delta between existing role claims and the expected catalog
    /// (ADR-0017 §8). Ordinal and case-sensitive. Only claims whose type is
    /// exactly <see cref="PermissionClaims.Type"/> are considered; every other
    /// claim type is out of jurisdiction and appears in neither output, so
    /// applying the delta preserves it.
    /// <list type="bullet">
    /// <item><see cref="Claim"/>s in <c>ToRemove</c>: one entry per occurrence
    /// to delete — every stale value (not in the catalog, e.g. <c>users.manage</c>
    /// or <c>Forms.Manage</c>) and every duplicate occurrence of an expected
    /// value beyond the first.</item>
    /// <item><c>ToAdd</c>: exactly one entry per missing expected value, in
    /// catalog order.</item>
    /// </list>
    /// </summary>
    public static (IReadOnlyList<Claim> ToAdd, IReadOnlyList<Claim> ToRemove) ComputePermissionClaimDelta(
        IEnumerable<Claim> existingClaims,
        IReadOnlyCollection<string> expectedPermissions)
    {
        ArgumentNullException.ThrowIfNull(existingClaims);
        ArgumentNullException.ThrowIfNull(expectedPermissions);

        var expected = expectedPermissions.ToHashSet(StringComparer.Ordinal);
        var retainedExpected = new HashSet<string>(StringComparer.Ordinal);
        var toRemove = new List<Claim>();

        foreach (var claim in existingClaims)
        {
            if (!string.Equals(claim.Type, PermissionClaims.Type, StringComparison.Ordinal))
            {
                continue; // not a permission claim — never part of the delta
            }

            if (expected.Contains(claim.Value) && retainedExpected.Add(claim.Value))
            {
                continue; // first occurrence of an expected value — retained
            }

            // Stale value, or a duplicate of an expected value.
            toRemove.Add(claim);
        }

        var toAdd = new List<Claim>();
        foreach (var value in expectedPermissions)
        {
            if (!retainedExpected.Contains(value))
            {
                toAdd.Add(new Claim(PermissionClaims.Type, value));
            }
        }

        return (toAdd, toRemove);
    }

    /// <summary>
    /// Find-or-create the canonical role through <c>RoleManager</c> (which
    /// normalizes the name — required by the unique normalized-name index and
    /// by <c>FindByNameAsync</c> lookups). If creation fails, re-query by the
    /// canonical name: a concurrent creator's row means success (the unique
    /// index and Identity's duplicate-name validator make the loser fail);
    /// otherwise fail startup with the Identity errors (fail-closed).
    /// </summary>
    private async Task<(ApplicationRole Role, bool Created)> EnsureRoleAsync(
        string roleName, CancellationToken cancellationToken)
    {
        var existing = await roleManager.FindByNameAsync(roleName);
        if (existing is not null)
        {
            return (existing, false);
        }

        var role = new ApplicationRole { Id = Guid.NewGuid().ToString("N"), Name = roleName };

        // The RoleManager API takes no CancellationToken, so the token is
        // checked at each await boundary: cancellation that arrived while
        // FindByNameAsync was executing must prevent the create write.
        cancellationToken.ThrowIfCancellationRequested();

        var result = await roleManager.CreateAsync(role);
        if (result.Succeeded)
        {
            logger.LogInformation(
                "Reconciliation created canonical role {RoleName} (Result: {Result}).",
                roleName,
                nameof(IdentityResult.Success));
            return (role, true);
        }

        existing = await roleManager.FindByNameAsync(roleName);
        if (existing is not null)
        {
            logger.LogInformation(
                "Reconciliation found canonical role {RoleName} created concurrently (Result: {Result}).",
                roleName,
                nameof(IdentityResult.Success));
            return (existing, false);
        }

        throw IdentityFailure("create canonical role", roleName, permissionValue: null, result);
    }

    /// <summary>
    /// Logs the failed operation with role/permission context and the Identity
    /// error codes, then returns the exception the caller throws — no failure is
    /// ignored and no success is reported for a failed operation (AGENTS §4:
    /// expected failures return errors; startup must not continue with an
    /// unverified catalog).
    /// </summary>
    private InvalidOperationException IdentityFailure(
        string operation,
        string roleName,
        string? permissionValue,
        IdentityResult result)
    {
        var errors = string.Join("; ", result.Errors.Select(error => $"{error.Code}: {error.Description}"));

        logger.LogError(
            "Reconciliation failed to {Operation} for role {RoleName}{PermissionContext}. Identity errors: {IdentityErrors}",
            operation,
            roleName,
            permissionValue is null ? string.Empty : $" (permission {permissionValue})",
            errors);

        return new InvalidOperationException(
            $"Role/permission reconciliation failed to {operation} for role '{roleName}'" +
            (permissionValue is null ? string.Empty : $" with permission value '{permissionValue}'") +
            $": {errors}");
    }
}
