using GraduateRecords.Application.Common.Results;

namespace GraduateRecords.Application.Features.Administration;

/// <summary>
/// Stable error codes and user-facing Persian messages for the administration
/// use cases (AGENTS §4: expected failures carry a kind, a stable code and a
/// message; §16: user-visible text is Persian). Codes are never localized so
/// tests and logs can match them; messages never contain an identifier, a
/// credential or a stack trace (AGENTS §18).
/// </summary>
public static class AdministrationErrors
{
    public const string PermissionDeniedCode = "identity.permission.denied";
    public const string UserNotFoundCode = "identity.user.not-found";
    public const string UnsupportedRoleCode = "identity.role.unsupported";
    public const string SelfRoleChangeCode = "identity.role.self-change";
    public const string LastAdministratorCode = "identity.admin.last-administrator";
    public const string ConcurrentAdministratorChangeCode = "identity.admin.concurrent-change";
    public const string DuplicateNationalIdCode = "identity.user.duplicate-national-id";
    public const string UserCreateFailedCode = "identity.user.create-failed";
    public const string RoleMutationFailedCode = "identity.role.mutation-failed";
    public const string AdministratorRoleUnavailableCode = "identity.admin.role-unavailable";
    public const string MissingAdministratorPreconditionCode = "identity.admin.precondition-missing";

    public const string PermissionDeniedMessage = "شما دسترسی لازم برای این عملیات را ندارید.";
    public const string UserNotFoundMessage = "کاربر موردنظر یافت نشد.";
    public const string UnsupportedRoleMessage = "نقش انتخاب‌شده پشتیبانی نمی‌شود.";
    public const string SelfRoleChangeMessage = "امکان تغییر نقش‌های خودتان وجود ندارد.";
    public const string LastAdministratorMessage =
        "امکان حذف آخرین مدیر سامانه وجود ندارد؛ پیش از آن باید مدیر دیگری تعیین شود.";
    public const string ConcurrentAdministratorChangeMessage =
        "هم‌زمان تغییری روی نقش‌های مدیری اعمال شده است. لطفاً صفحه را تازه‌سازی کنید و دوباره تلاش کنید.";
    public const string DuplicateNationalIdMessage = "کاربری با این شناسه ملی قبلاً ثبت شده است.";
    public const string UserCreateFailedMessage = "ایجاد کاربر ناموفق بود؛ لطفاً دوباره تلاش کنید.";
    public const string RoleMutationFailedMessage = "تغییر نقش کاربر ناموفق بود؛ لطفاً دوباره تلاش کنید.";
    public const string AdministratorRoleUnavailableMessage =
        "نقش مدیر سامانه در دسترس نیست؛ عملیات انجام نشد.";
    public const string MissingAdministratorPreconditionMessage =
        "پیش‌شرط بررسی آخرین مدیر فراهم نشد؛ عملیات انجام نشد.";

    public static Error PermissionDenied() =>
        new(ErrorKind.Forbidden, PermissionDeniedCode, PermissionDeniedMessage);

    public static Error UserNotFound() =>
        new(ErrorKind.NotFound, UserNotFoundCode, UserNotFoundMessage);

    public static Error UnsupportedRole() =>
        new(ErrorKind.Validation, UnsupportedRoleCode, UnsupportedRoleMessage);

    public static Error SelfRoleChange() =>
        new(ErrorKind.Forbidden, SelfRoleChangeCode, SelfRoleChangeMessage);

    public static Error LastAdministrator() =>
        new(ErrorKind.Conflict, LastAdministratorCode, LastAdministratorMessage);

    public static Error ConcurrentAdministratorChange() =>
        new(ErrorKind.Conflict, ConcurrentAdministratorChangeCode, ConcurrentAdministratorChangeMessage);

    public static Error Validation(string message) =>
        new(ErrorKind.Validation, "identity.validation", message);

    public static Error DuplicateNationalId() =>
        new(ErrorKind.Validation, DuplicateNationalIdCode, DuplicateNationalIdMessage);

    public static Error UserCreateFailed() =>
        new(ErrorKind.Unavailable, UserCreateFailedCode, UserCreateFailedMessage);

    public static Error RoleMutationFailed() =>
        new(ErrorKind.Unavailable, RoleMutationFailedCode, RoleMutationFailedMessage);

    public static Error AdministratorRoleUnavailable() =>
        new(ErrorKind.Unavailable, AdministratorRoleUnavailableCode, AdministratorRoleUnavailableMessage);

    public static Error MissingAdministratorPrecondition() =>
        new(ErrorKind.Unavailable, MissingAdministratorPreconditionCode, MissingAdministratorPreconditionMessage);
}
