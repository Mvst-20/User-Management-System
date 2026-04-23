using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public interface ITokenService
{
    Task<EmailVerificationToken?> GetVerificationTokenAsync(string token);
    Task<EmailVerificationToken> CreateVerificationTokenAsync(ulong userId, VerificationTokenType type, string? newEmail = null);
    Task DeleteVerificationTokenAsync(ulong tokenId);
    Task DeleteExpiredTokensAsync();
}
