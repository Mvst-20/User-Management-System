using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public class UserService : IUserService
{
    private readonly ApplicationDbContext _context;
    private readonly IOnlineUserService _onlineUserService;

    public UserService(ApplicationDbContext context, IOnlineUserService onlineUserService)
    {
        _context = context;
        _onlineUserService = onlineUserService;
    }

    public async Task<User?> GetByIdAsync(ulong id)
    {
        return await _context.Users.FindAsync(id);
    }

    public async Task<User?> GetByUsernameAsync(string username)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username);
    }

    public async Task<User?> GetByEmailAsync(string email)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Email == email);
    }

    public async Task<User?> GetByPhoneAsync(string phone)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Phone == phone);
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
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<(List<User> Users, int TotalCount)> GetUsersAsync(
        int page, int pageSize, int? status, string? search)
    {
        var query = _context.Users.AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(u => u.Status == (UserStatus)status.Value);
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

    public int GetOnlineUsersCount()
    {
        return _onlineUserService.GetOnlineUsersCount();
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
        // 检查是否已有用户被加入超级管理员组（排除系统用户 uid=0）
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
