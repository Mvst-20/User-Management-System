using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public class UserService : IUserService
{
    private readonly ApplicationDbContext _context;
    private readonly IOnlineUserService _onlineUserService;
    private readonly IPermissionService _permissionService;
    private readonly IMemoryCache _cache;

    public UserService(ApplicationDbContext context, IOnlineUserService onlineUserService, IPermissionService permissionService, IMemoryCache cache)
    {
        _context = context;
        _onlineUserService = onlineUserService;
        _permissionService = permissionService;
        _cache = cache;
    }

    /// <summary>
    /// OnTokenValidated 中的用户状态缓存键（短 TTL）。
    /// 用户状态发生变更（删除/封禁）时必须清除对应缓存。
    /// </summary>
    public static string UserStatusCacheKey(ulong userId) => $"user_status_{userId}";

    public async Task<User?> GetByIdAsync(ulong id)
    {
        return await _context.Users.FindAsync(id);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<User> CreateUserAsync(User user)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<User> UpdateUserAsync(User user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<bool> DeleteUserAsync(ulong id)
    {
        // 保护系统初始用户 (uid=0) 不被删除
        if (id == 0)
            return false;

        var user = await _context.Users.FindAsync(id);
        if (user == null) return false;

        user.Status = UserStatus.Deleted;
        user.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // 清除被删除用户的权限缓存与状态缓存
        await _permissionService.InvalidateUserPermissionCacheAsync(id);
        _cache.Remove(UserStatusCacheKey(id));

        return true;
    }

    public async Task<(List<User> Users, int TotalCount)> GetUsersAsync(
        int page, int pageSize, int? status, string? search)
    {
        var query = _context.Users.AsQueryable();

        if (status.HasValue)
        {
            var statusEnum = (UserStatus)status.Value;
            if (!Enum.IsDefined(statusEnum))
                throw new ArgumentException($"无效的用户状态值: {status.Value}");
            query = query.Where(u => u.Status == statusEnum);
        }
        else
        {
            query = query.Where(u => u.Status != UserStatus.Deleted);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(u =>
                u.Username.Contains(search) ||
                u.Email.Contains(search) ||
                (u.Phone != null && u.Phone.Contains(search)));
        }

        var totalCount = await query.CountAsync();

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (users, totalCount);
    }

    public async Task<int> GetOnlineUsersCountAsync()
    {
        return await _onlineUserService.GetOnlineUsersCountAsync();
    }

    public async Task<int> GetTotalUsersCountAsync()
    {
        return await _context.Users
            .Where(u => u.Status != UserStatus.Deleted)
            .CountAsync();
    }

    public async Task<int> GetNewUsersCountAsync(int days)
    {
        var startDate = DateTime.UtcNow.AddDays(-days);
        return await _context.Users
            .Where(u => u.CreatedAt >= startDate && u.Status != UserStatus.Deleted)
            .CountAsync();
    }

    public async Task<int> GetActiveUsersCountAsync(int days)
    {
        var startDate = DateTime.UtcNow.AddDays(-days);
        return await _context.Users
            .Where(u => u.LastLoginAt >= startDate && u.Status != UserStatus.Deleted)
            .CountAsync();
    }

    public async Task<bool> HasAdminAsync()
    {
        return await _context.UserGroupMembers
            .Include(m => m.Group)
            .AnyAsync(m => m.Group.Name == PermissionCodes.SuperAdminGroupName && m.UserId != 0);
    }

    public async Task<User?> ValidateCredentialsAsync(string login, string password)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u =>
                u.Username == login || u.Email == login);

        if (user == null) return null;

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            return null;

        return user;
    }

    public async Task<bool> ActiveUsernameExistsAsync(string username)
    {
        return await _context.Users
            .AnyAsync(u => u.Username == username && u.Status != UserStatus.Deleted);
    }

    public async Task<bool> ActiveEmailExistsAsync(string email)
    {
        return await _context.Users
            .AnyAsync(u => u.Email == email && u.Status != UserStatus.Deleted);
    }

    public async Task<bool> ActivePhoneExistsAsync(string phone, ulong? excludeUserId = null)
    {
        var query = _context.Users.Where(u => u.Phone == phone && u.Status != UserStatus.Deleted);
        if (excludeUserId.HasValue)
            query = query.Where(u => u.Id != excludeUserId.Value);
        return await query.AnyAsync();
    }
}
