namespace GraduateRecords.Application.Common.Identity;

/// <summary>The minimal local-identity projection the Application layer may see.</summary>
public sealed record LocalUser(string Id, string FullName);

/// <summary>
/// Application port to the local Identity user store (implemented in
/// Infrastructure). It exists so the sign-in use case can resolve a user by
/// National ID without Identity types entering the Application layer.
/// </summary>
public interface ILocalUserDirectory
{
    Task<LocalUser?> FindByNationalIdAsync(string normalizedNationalId, CancellationToken cancellationToken = default);
}
