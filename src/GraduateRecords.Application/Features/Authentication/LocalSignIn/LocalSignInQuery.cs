using GraduateRecords.Application.Common.Messaging;

namespace GraduateRecords.Application.Features.Authentication.LocalSignIn;

/// <summary>
/// The post-validation decision of the local credential login (ADR-0015 §1, §4,
/// §6, §9): validate the entered National ID with the same canonical rules as
/// the SSO path, resolve the local user, and permit the password check only for
/// eligible Staff/Administrator accounts. The query carries only HTTP-derived
/// metadata and the raw identifier input — never a password.
/// </summary>
public sealed record LocalSignInQuery(
    string? RawNationalId,
    string? ClientIpAddress,
    string? UserAgent,
    string CorrelationId) : IQuery<LocalSignInTicket>;
