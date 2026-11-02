using Microsoft.AspNetCore.Authorization;

namespace GraduateRecords.Web.Authorization;

/// <summary>
/// Authorization requirement for one canonical permission value (ADR-0017 §5:
/// PascalCase.Dot identifiers; ARCHITECTURE §10: "Controller attribute gates the
/// action type"). The requirement is evaluated against the session principal's
/// <c>permission</c> claims only — never <c>UserKind</c> and never the
/// authentication method (ADR-0017 §1, §4, §21).
/// </summary>
public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;
