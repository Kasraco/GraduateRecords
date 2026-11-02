using System.Text;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Security;

namespace GraduateRecords.Web.Models.Admin;

/// <summary>Keys of the two TempData slots the shared layout renders as a toast.</summary>
public static class TempDataKeys
{
    public const string Success = "Success";
    public const string Error = "Error";
}

/// <summary>Display labels for canonical values. Labels are presentation only —
/// the persisted/authorization values stay the canonical constants.</summary>
public static class AdminDisplayLabels
{
    /// <summary>Persian label for a canonical role name; unknown names fall back to the raw value.</summary>
    public static string Role(string roleName) => roleName switch
    {
        RoleNames.Student => "دانشجو",
        RoleNames.GraduateStaff => "کارشناس تحصیلات تکمیل",
        RoleNames.Administrator => "مدیر سامانه",
        _ => roleName,
    };

    /// <summary>Persian label for an account kind (authentication eligibility only — never authorization).</summary>
    public static string Kind(LocalUserKind kind) => kind switch
    {
        LocalUserKind.Student => "دانشجو",
        LocalUserKind.Staff => "کارمند",
        LocalUserKind.Administrator => "مدیر",
        _ => kind.ToString(),
    };

    /// <summary>Shown when a role carries no Phase 1A feature permissions (ADR-0017 §8).</summary>
    public const string NoPermissions = "بدون دسترسی ویژه";

    /// <summary>
    /// Persian digits for display numbers (DESIGN §4: Persian digits in display
    /// text). .NET's numeric formatting never uses <c>NativeDigits</c>, so the
    /// mapping is explicit. Formatting only — canonical data is never stored
    /// this way (AGENTS §16), and this is not a second identifier normalizer.
    /// </summary>
    public static string PersianDigits(int value)
    {
        var digits = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var builder = new StringBuilder(digits.Length);
        foreach (var c in digits)
        {
            builder.Append(c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c);
        }

        return builder.ToString();
    }
}

/// <summary>Row of the administration user list.</summary>
public sealed class UserRowViewModel
{
    public required string Id { get; init; }

    public required string FullName { get; init; }

    public required string NationalIdMasked { get; init; }

    public required LocalUserKind Kind { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }
}

/// <summary>User list page (ADR-0004 §8.2: account data only).</summary>
public sealed class UsersIndexViewModel
{
    public const int PageSize = 20;

    public string? Query { get; init; }

    public int Page { get; init; } = 1;

    public int TotalCount { get; init; }

    public IReadOnlyList<UserRowViewModel> Users { get; init; } = Array.Empty<UserRowViewModel>();

    public bool HasPrevious => Page > 1;

    public bool HasNext => Page * PageSize < TotalCount;

    public int TotalPages => Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
}

/// <summary>User detail plus the roles that can still be granted.</summary>
public sealed class UserDetailsViewModel
{
    public required string Id { get; init; }

    public required string FullName { get; init; }

    public required string NationalIdMasked { get; init; }

    public required LocalUserKind Kind { get; init; }

    public required IReadOnlyList<string> Roles { get; init; }

    /// <summary>Supported roles the user does not hold yet (the grant form's options).</summary>
    public required IReadOnlyList<string> AvailableRoles { get; init; }

    /// <summary>True when the actor is looking at their own account (§20 blocks any change there).</summary>
    public required bool IsOwnAccount { get; init; }
}

/// <summary>Create-user form (ADR-0002 §2.1). Never bound to a command or entity.</summary>
public sealed class CreateUserViewModel
{
    public string? NationalId { get; set; }

    public string? FullName { get; set; }

    public string? MobileNumber { get; set; }

    public LocalUserKind Kind { get; set; } = LocalUserKind.Staff;

    public List<string> Roles { get; set; } = [];

    /// <summary>Kinds an Administrator may create by hand (students are JIT-provisioned).</summary>
    public IReadOnlyList<LocalUserKind> AllowedKinds { get; } =
        [LocalUserKind.Staff, LocalUserKind.Administrator];

    public IReadOnlyList<string> SupportedRoles { get; } = RoleNames.AllSupported;
}

/// <summary>Read-only role → permission page (ADR-0017 §6/§10: no mapping editor in v1).</summary>
public sealed class RolesIndexViewModel
{
    public IReadOnlyList<RolePermissionRowViewModel> Roles { get; init; } =
        Array.Empty<RolePermissionRowViewModel>();
}

public sealed class RolePermissionRowViewModel
{
    public required string RoleName { get; init; }

    public required IReadOnlyList<string> Permissions { get; init; }
}
