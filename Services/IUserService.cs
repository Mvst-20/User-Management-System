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
    int GetOnlineUsersCount();
    Task<int> GetNewUsersCountAsync(int days);
    Task<int> GetActiveUsersCountAsync(int days);
    Task<bool> HasAdminAsync();
    Task<User?> ValidateCredentialsAsync(string login, string password);

    /// <summary>
    /// 检查非删除状态的用户名是否已存在（用于注册/更新唯一性校验）
    /// </summary>
    Task<bool> ActiveUsernameExistsAsync(string username);

    /// <summary>
    /// 检查非删除状态的邮箱是否已存在（用于注册/更新唯一性校验）
    /// </summary>
    Task<bool> ActiveEmailExistsAsync(string email);

    /// <summary>
    /// 检查非删除状态的手机号是否已存在（用于注册/更新唯一性校验）
    /// </summary>
    Task<bool> ActivePhoneExistsAsync(string phone, ulong? excludeUserId = null);
}
