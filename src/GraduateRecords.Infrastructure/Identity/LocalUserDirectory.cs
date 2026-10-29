using GraduateRecords.Application.Common.Identity;
using Microsoft.AspNetCore.Identity;

namespace GraduateRecords.Infrastructure.Identity;

/// <summary>Resolves local users by National ID through the Identity user store.</summary>
public sealed class LocalUserDirectory(UserManager<ApplicationUser> userManager) : ILocalUserDirectory
{
    public async Task<LocalUser?> FindByNationalIdAsync(string normalizedNationalId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedNationalId);

        // UserName is the normalized National ID (ADR-0001 rule 6, ADR-0015 §3).
        var user = await userManager.FindByNameAsync(normalizedNationalId);
        return user is null ? null : new LocalUser(user.Id, user.FullName, user.UserKind);
    }
}
