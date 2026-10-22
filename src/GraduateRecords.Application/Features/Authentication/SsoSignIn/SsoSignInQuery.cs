using GraduateRecords.Application.Common.Messaging;

namespace GraduateRecords.Application.Features.Authentication.SsoSignIn;

/// <summary>
/// The post-authentication decision of the SSO callback (ADR-0001 rules 5–9):
/// validate the National ID claim, then resolve the local user. The query carries
/// only HTTP-derived metadata (never the raw claim value in audit or logs).
/// </summary>
public sealed record SsoSignInQuery(
    string? RawNationalIdClaim,
    string? ClientIpAddress,
    string? UserAgent,
    string CorrelationId) : IQuery<SsoSignInTicket>;
