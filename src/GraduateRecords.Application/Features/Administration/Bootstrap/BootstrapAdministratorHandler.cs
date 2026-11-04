using GraduateRecords.Application.Common.Auditing;
using GraduateRecords.Application.Common.Identity;
using GraduateRecords.Application.Common.Messaging;
using GraduateRecords.Application.Common.Results;
using GraduateRecords.Application.Common.Security;
using GraduateRecords.Application.Common.Time;
using GraduateRecords.Domain.Auditing;
using GraduateRecords.Domain.Common;

namespace GraduateRecords.Application.Features.Administration.Bootstrap;

/// <summary>
/// Startup Bootstrap of the first Administrator (ADR-0016 §1–§4).
/// <para>
/// <b>Order of decisions (each step is documented in the ADR it implements):</b>
/// </para>
/// <list type="number">
/// <item>Development with no configuration → <see cref="BootstrapOutcome.SkippedDevelopment"/>:
/// the §14.2 Development allowance needs no database access at all, so an
/// inspection-only start never touches persistence.</item>
/// <item><b>Completion guard (§4.2):</b> while any account holds the
/// Administrator role the step is completely inert — whatever the configuration
/// says (§4.3). No writes, no audit, startup proceeds (decision D2).</item>
/// <item>No Administrator exists → configuration is mandatory outside
/// Development (§14.2); present-but-invalid/partial configuration is refused in
/// every environment (§2.4/§14.1 — the composition root's
/// <c>ValidateOnStart</c> already rejects it before this handler runs; the
/// branch keeps the handler fail-closed on its own).</item>
/// <item>Target state (§4.5/§14.4/§14.5): an existing Student or Staff
/// identity is never elevated automatically.</item>
/// <item>Both write paths go through
/// <see cref="IIdentityAdministration.EstablishFirstAdministratorAsync"/> only
/// — the single atomic lock → re-check → write → commit operation (decision D8).
/// </item>
/// </list>
/// <para>
/// Refusals and failures are audited with a fixed non-sensitive summary and no
/// identifier payload (§12.2/§12.3, decision D6: empty <c>EntityId</c> when no
/// user id exists). <c>AlreadyEstablished</c> and <c>SkippedDevelopment</c>
/// write no audit event at all. Phase 1A audit is best-effort Serilog only —
/// no transactional persistence is claimed (§12.4).
/// </para>
/// </summary>
public sealed class BootstrapAdministratorHandler(
    ILocalUserDirectory users,
    IIdentityAdministration identity,
    IAuditWriter auditWriter,
    IClock clock) : ICommandHandler<BootstrapAdministratorCommand, BootstrapOutcome>
{
    /// <summary>D4: fixed display name — never part of configuration (ADR-0016 §2.5).</summary>
    public const string BootstrapDisplayName = "مدیر سامانه";

    /// <summary>Application cannot reference ASP.NET Core (AGENTS §2); the environment name arrives as a string.</summary>
    private const string DevelopmentEnvironmentName = "Development";

    // Fixed, non-sensitive audit summaries (ADR-0016 §12.2). None contains an
    // identifier, a mobile number, a configuration value or a credential.
    public const string ProvisionedSummary = "نخستین مدیر سامانه ایجاد شد.";
    public const string RoleEstablishedSummary = "نقش مدیر سامانه برای حساب پیش‌تر ایجادشده فعال شد.";
    public const string ConfigurationRefusedSummary =
        "ایجاد نخستین مدیر سامانه انجام نشد: پیکربندی نامعتبر یا ناقص است.";
    public const string ConfigurationMissingSummary =
        "ایجاد نخستین مدیر سامانه انجام نشد: پیکربندی ارائه نشده است.";
    public const string TargetRefusedSummary =
        "ایجاد خودکار مدیر سامانه برای این حساب مجاز نیست.";
    public const string ProvisioningFailedSummary = "ایجاد نخستین مدیر سامانه ناموفق بود.";

    public async Task<Result<BootstrapOutcome>> HandleAsync(
        BootstrapAdministratorCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var isDevelopment = string.Equals(
            command.EnvironmentName, DevelopmentEnvironmentName, StringComparison.OrdinalIgnoreCase);

        var nationalId = NormalizeOptionalIdentifier(command.NationalId);
        var mobile = NormalizeOptionalMobile(command.MobileNumber);

        var absent = nationalId is null && mobile is null;
        var partial = (nationalId is null) != (mobile is null);

        // 1. §14.2 Development allowance: absent configuration starts for
        //    inspection/testing only, performs no writes and needs no database
        //    access (so a Development machine without persistence still boots).
        if (absent && isDevelopment)
        {
            return Result<BootstrapOutcome>.Success(BootstrapOutcome.SkippedDevelopment);
        }

        // 2. Completion guard (§4.2). Runs before any configuration check so
        //    that blanking the configuration after completion stays legitimate
        //    (§2.4/§4.3): an existing Administrator makes the step inert.
        var holders = await identity.GetEffectiveAdministratorIdsAsync(cancellationToken);
        if (holders.Count > 0)
        {
            return Result<BootstrapOutcome>.Success(BootstrapOutcome.AlreadyEstablished);
        }

        // 3. No Administrator exists yet: configuration is mandatory outside
        //    Development (§14.2) and must be complete and valid (§2.4/§14.1).
        if (absent || partial)
        {
            var error = partial
                ? AdministrationErrors.BootstrapConfigurationInvalid()
                : AdministrationErrors.BootstrapConfigurationMissing();
            var summary = partial ? ConfigurationRefusedSummary : ConfigurationMissingSummary;

            await WriteAuditAsync(
                AuditActions.BootstrapAdministratorProvisioned,
                entityId: string.Empty, // D6: no user id exists yet
                AuditResult.Denied,
                summary,
                cancellationToken);

            return Result<BootstrapOutcome>.Failure(error);
        }

        if (!NationalId.TryParse(nationalId, out _))
        {
            await WriteAuditAsync(
                AuditActions.BootstrapAdministratorProvisioned,
                entityId: string.Empty,
                AuditResult.Denied,
                ConfigurationRefusedSummary,
                cancellationToken);
            return Result<BootstrapOutcome>.Failure(AdministrationErrors.BootstrapConfigurationInvalid());
        }

        if (!IsValidMobile(mobile))
        {
            await WriteAuditAsync(
                AuditActions.BootstrapAdministratorProvisioned,
                entityId: string.Empty,
                AuditResult.Denied,
                ConfigurationRefusedSummary,
                cancellationToken);
            return Result<BootstrapOutcome>.Failure(AdministrationErrors.BootstrapConfigurationInvalid());
        }

        // 4. Target state (§4.5): refuse automatic elevation of an existing
        //    Student (§14.4) or Staff (§14.5) identity. Never any write.
        var target = await users.FindByNationalIdAsync(nationalId!, cancellationToken);
        if (target is not null && target.Kind is not LocalUserKind.Administrator)
        {
            await WriteAuditAsync(
                AuditActions.BootstrapAdministratorProvisioned,
                entityId: target.Id,
                AuditResult.Denied,
                TargetRefusedSummary,
                cancellationToken);
            return Result<BootstrapOutcome>.Failure(AdministrationErrors.BootstrapRefused());
        }

        // 5. The only write path (decision D8): lock → effective-Administrator
        //    re-check → create-or-establish → commit, in one transaction.
        var established = await identity.EstablishFirstAdministratorAsync(
            new CreateIdentityUser(
                nationalId!,
                BootstrapDisplayName,
                mobile!,
                LocalUserKind.Administrator,
                [RoleNames.Administrator]),
            cancellationToken);

        if (!established.IsSuccess)
        {
            await WriteAuditAsync(
                AuditActions.BootstrapAdministratorProvisioned,
                // The target's id when it was known before the call; otherwise
                // empty (D6). Never the National ID itself.
                entityId: target?.Id ?? string.Empty,
                AuditResult.Failed,
                ProvisioningFailedSummary,
                cancellationToken);
            return Result<BootstrapOutcome>.Failure(
                established.Error ?? AdministrationErrors.UserCreateFailed());
        }

        switch (established.Value)
        {
            case FirstAdministratorEstablishment.AlreadyEstablished:
                // D2 / §4.2: a competing instance established the Administrator
                // between our guard read and our lock — inert, no audit.
                return Result<BootstrapOutcome>.Success(BootstrapOutcome.AlreadyEstablished);

            case FirstAdministratorEstablishment.RoleEstablished:
            {
                // Resume (§4.5): the identity existed before this call, so its
                // id is known after the fact through the same lookup.
                var establishedUser = await users.FindByNationalIdAsync(nationalId!, cancellationToken);
                await WriteAuditAsync(
                    AuditActions.BootstrapAdministratorRoleEstablished,
                    entityId: establishedUser?.Id ?? string.Empty,
                    AuditResult.Success,
                    RoleEstablishedSummary,
                    cancellationToken);
                return Result<BootstrapOutcome>.Success(BootstrapOutcome.RoleEstablished);
            }

            default:
            {
                var createdUser = await users.FindByNationalIdAsync(nationalId!, cancellationToken);
                await WriteAuditAsync(
                    AuditActions.BootstrapAdministratorProvisioned,
                    entityId: createdUser?.Id ?? string.Empty,
                    AuditResult.Success,
                    ProvisionedSummary,
                    cancellationToken);
                return Result<BootstrapOutcome>.Success(BootstrapOutcome.Created);
            }
        }
    }

    /// <summary>
    /// One audit record per operation (ADR-0016 §12.2): no actor (bootstrap runs
    /// without a session), <c>EntityType = User</c>, fixed summary and a null
    /// details payload — never a National ID, a mobile number or a credential
    /// (§12.3, AGENTS §14).
    /// </summary>
    private Task WriteAuditAsync(
        string action,
        string entityId,
        AuditResult result,
        string summary,
        CancellationToken cancellationToken) =>
        auditWriter.WriteAsync(
            new AuditEntry(
                clock.UtcNow,
                ActorUserId: null,
                ActorDisplayName: null,
                action,
                EntityType: "User",
                entityId,
                result,
                summary,
                DetailsJson: null,
                IpAddress: null,
                UserAgent: null,
                CorrelationId: null),
            cancellationToken);

    private static string? NormalizeOptionalIdentifier(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : PersianTextNormalizer.NormalizeIdentifier(value);

    private static string? NormalizeOptionalMobile(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : PersianTextNormalizer.ToLatinDigits(value).Trim();

    /// <summary>
    /// Canonical mobile shape (§16): <c>09xxxxxxxxx</c>. Mirrors the private
    /// rule in <c>CreateUserCommand</c>; a deliberate three-line duplication —
    /// Application must not grow a shared "helpers" root (AGENTS §3).
    /// </summary>
    private static bool IsValidMobile(string? raw)
    {
        var mobile = NormalizeOptionalMobile(raw) ?? string.Empty;
        if (mobile.Length != 11 || mobile[0] != '0' || mobile[1] != '9')
        {
            return false;
        }

        foreach (var character in mobile)
        {
            if (!char.IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }
}
