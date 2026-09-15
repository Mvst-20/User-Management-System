using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Data;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public class OnlineUserService : IOnlineUserService
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    private readonly ApplicationDbContext _context;

    public OnlineUserService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task MarkUserOnlineAsync(string userId)
    {
        var now = DateTime.UtcNow;
        var existing = await _context.OnlineUsers.FindAsync(userId);
        if (existing == null)
        {
            _context.OnlineUsers.Add(new OnlineUser { UserId = userId, LastActiveAt = now });
        }
        else
        {
            existing.LastActiveAt = now;
        }
        await _context.SaveChangesAsync();
    }

    public async Task MarkUserOfflineAsync(string userId)
    {
        await _context.OnlineUsers
            .Where(u => u.UserId == userId)
            .ExecuteDeleteAsync();
    }

    public async Task<int> GetOnlineUsersCountAsync()
    {
        var threshold = DateTime.UtcNow - DefaultTimeout;
        return await _context.OnlineUsers.CountAsync(u => u.LastActiveAt > threshold);
    }

    public async Task CleanExpiredUsersAsync(TimeSpan? timeout = null)
    {
        var threshold = DateTime.UtcNow - (timeout ?? DefaultTimeout);
        await _context.OnlineUsers
            .Where(u => u.LastActiveAt < threshold)
            .ExecuteDeleteAsync();
    }
}
