namespace GraduateRecords.Application.Features.Authentication.LocalSignIn;

/// <summary>
/// The resolved local identity that may proceed to the password check. The
/// password itself is verified by ASP.NET Core Identity plumbing in
/// AccountController (AGENTS §2.2) and never enters Application.
/// </summary>
public sealed record LocalSignInTicket(string UserId, string FullName);
