namespace GraduateRecords.Application.Features.Authentication.SsoSignIn;

/// <summary>The resolved local identity for an authenticated SSO session.</summary>
public sealed record SsoSignInTicket(string UserId, string FullName);
