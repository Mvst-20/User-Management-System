using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public interface IUserService
{
    Task<User?> GetByIdAsync(ulong id);
    Task<User?> GetByUsernameAsync(string username);
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByPhoneAsync(string phone);
    Task<User> CreateUserAsync(User user);
    Task<User> UpdateUserAsync(User user);
    Task<bool> DeleteUserAsync(ulong id);
    Task<(List<User> Users, int TotalCount)> GetUsersAsync(int page, int pageSize, int? status, int? role, string? search);
    void MarkUserOnline(string userId);
    void MarkUserOffline(string userId);
    int GetOnlineUsersCount();
    Task<int> GetOnlineUsersCountAsync();
    Task<int> GetNewUsersCountAsync(int days);
    Task<int> GetActiveUsersCountAsync(int days);
    Task<bool> HasAdminAsync();
    Task<User?> ValidateCredentialsAsync(string login, string password);
}
