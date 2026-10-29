namespace GraduateRecords.Application.Common.Identity;

/// <summary>
/// Account kinds of the local identity model (ADR-0002 data model
/// <c>UserKind</c>; ADR-0015 §2 authentication matrix). The kind decides which
/// authentication paths an account may use: Students are SSO-only, while Staff
/// and Administrator accounts may additionally hold local credentials.
/// </summary>
public enum LocalUserKind
{
    /// <summary>SSO-only (ADR-0015 §6). Also the fail-closed default for unclassified accounts.</summary>
    Student = 0,

    Staff = 1,

    Administrator = 2,
}

/// <summary>The minimal local-identity projection the Application layer may see.</summary>
public sealed record LocalUser(string Id, string FullName, LocalUserKind Kind);

/// <summary>
/// Application port to the local Identity user store (implemented in
/// Infrastructure). It exists so the sign-in use cases can resolve a user by
/// National ID without Identity types entering the Application layer.
/// </summary>
public interface ILocalUserDirectory
{
    Task<LocalUser?> FindByNationalIdAsync(string normalizedNationalId, CancellationToken cancellationToken = default);
}
