using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public class UserService : IUserService
{
    private readonly ApplicationDbContext _context;
    private readonly ISessionService _sessionService;
    private readonly IPermissionService _permissionService;
    private readonly ICacheVersionService _cacheVersionService;

    public UserService(
        ApplicationDbContext context,
        ISessionService sessionService,
        IPermissionService permissionService,
        ICacheVersionService cacheVersionService)
    {
        _context = context;
        _sessionService = sessionService;
        _permissionService = permissionService;
        _cacheVersionService = cacheVersionService;
    }

    /// <summary>
    /// 鉴权链路中的用户状态缓存键。键内嵌 <c>user_status</c> 作用域版本号，
    /// 状态变更时只需递增版本号，所有实例的旧键随之失效。
    /// </summary>
    public static string UserStatusCacheKey(long version, ulong userId) => $"status:v{version}:user:{userId}";

    public async Task<User?> GetByIdAsync(ulong id, CancellationToken ct = default)
    {
        return await _context.Users.FindAsync(new object?[] { id }, ct);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email && u.Status != UserStatus.Deleted);
    }

    public async Task<User> CreateUserAsync(User user)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task<User> UpdateUserAsync(User user)
    {
        // 仅当状态真正变化时才递增版本号：登录成功也会走本方法更新 LastLoginAt，
        // 若无条件 bump 会造成每次登录都全局失效一次（写放大）。
        var previousStatus = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == user.Id)
            .Select(u => (UserStatus?)u.Status)
            .FirstOrDefaultAsync();

        _context.Users.Update(user);
        await _context.SaveChangesAsync();

        if (previousStatus.HasValue && previousStatus.Value != user.Status)
        {
            await _cacheVersionService.BumpAsync(CacheScopes.UserStatus);
        }

        return user;
    }

    public async Task<bool> DeleteUserAsync(ulong id)
    {
        // 保护系统初始用户 (uid=0) 不被删除
        if (id == 0)
            return false;

        var user = await _context.Users.FindAsync(id);
        if (user == null) return false;

        // 幂等：已软删除的用户再次删除时保留首次 deleted_at，供“注销用户”按日统计使用
        if (user.Status == UserStatus.Deleted)
            return true;

        user.Status = UserStatus.Deleted;
        user.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // 递增权限与用户状态版本号，所有实例立即感知
        await _permissionService.InvalidateAllPermissionCacheAsync();
        await _cacheVersionService.BumpAsync(CacheScopes.UserStatus);

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

    /// <summary>
    /// 在线用户数：由会话的最近活跃时间派生（30 分钟内的活跃会话去重用户数），
    /// 取代原 online_users 表——那张表一个用户只有一行，无法表达多设备。
    /// </summary>
    public Task<int> GetOnlineUsersCountAsync() => _sessionService.GetOnlineUserCountAsync();

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

        if (user == null)
        {
            // 用户不存在时也执行一次同成本的 BCrypt 校验，消除账号枚举的时序侧信道
            PasswordHasher.VerifyDummy(password);
            return null;
        }

        if (!PasswordHasher.Verify(password, user.PasswordHash))
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
