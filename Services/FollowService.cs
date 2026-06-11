using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public class FollowService : IFollowService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<FollowService> _logger;

    public FollowService(ApplicationDbContext context, ILogger<FollowService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<FollowResponse> FollowAsync(ulong followerId, ulong followingId)
    {
        if (followerId == followingId)
        {
            throw new InvalidOperationException("不能关注自己");
        }

        // 检查目标用户是否存在且状态正常
        var targetUser = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == followingId && u.Status != UserStatus.Deleted);

        if (targetUser == null)
        {
            throw new KeyNotFoundException("用户不存在");
        }

        // 检查是否已关注
        var existingFollow = await _context.UserFollows
            .FirstOrDefaultAsync(f => f.FollowerId == followerId && f.FollowingId == followingId);

        if (existingFollow != null)
        {
            throw new InvalidOperationException("已经关注了该用户");
        }

        var follow = new UserFollow
        {
            FollowerId = followerId,
            FollowingId = followingId,
            CreatedAt = DateTime.UtcNow
        };

        _context.UserFollows.Add(follow);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {FollowerId} followed user {FollowingId}", followerId, followingId);

        return new FollowResponse(
            followingId,
            targetUser.Username,
            targetUser.AvatarUrl,
            true,
            follow.CreatedAt
        );
    }

    public async Task<bool> UnfollowAsync(ulong followerId, ulong followingId)
    {
        var follow = await _context.UserFollows
            .FirstOrDefaultAsync(f => f.FollowerId == followerId && f.FollowingId == followingId);

        if (follow == null)
        {
            return false;
        }

        _context.UserFollows.Remove(follow);
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {FollowerId} unfollowed user {FollowingId}", followerId, followingId);

        return true;
    }

    public async Task<bool> IsFollowingAsync(ulong followerId, ulong followingId)
    {
        return await _context.UserFollows
            .AnyAsync(f => f.FollowerId == followerId && f.FollowingId == followingId);
    }

    public async Task<FollowListResponse> GetFollowingListAsync(ulong userId, int page, int pageSize, ulong? currentUserId = null)
    {
        // 检查关注列表可见性
        if (currentUserId != userId)
        {
            var isHidden = await _context.Users
                .Where(u => u.Id == userId)
                .Select(u => u.IsFollowingListHidden)
                .FirstOrDefaultAsync();

            if (isHidden)
            {
                return new FollowListResponse(new List<FollowListItem>(), 0, page, pageSize);
            }
        }

        var query = _context.UserFollows
            .Where(f => f.FollowerId == userId)
            .Include(f => f.Following)
            .Where(f => f.Following.Status != UserStatus.Deleted);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new FollowListItem(
                f.FollowingId,
                f.Following.Username,
                f.Following.AvatarUrl,
                f.CreatedAt
            ))
            .ToListAsync();

        return new FollowListResponse(items, totalCount, page, pageSize);
    }

    public async Task<FollowListResponse> GetFollowersListAsync(ulong userId, int page, int pageSize, ulong? currentUserId = null)
    {
        var query = _context.UserFollows
            .Where(f => f.FollowingId == userId)
            .Include(f => f.Follower)
            .Where(f => f.Follower.Status != UserStatus.Deleted);

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new FollowListItem(
                f.FollowerId,
                f.Follower.Username,
                f.Follower.AvatarUrl,
                f.CreatedAt
            ))
            .ToListAsync();

        return new FollowListResponse(items, totalCount, page, pageSize);
    }

    public async Task<FollowStatsResponse> GetFollowStatsAsync(ulong userId)
    {
        var followingCount = await _context.UserFollows
            .Where(f => f.FollowerId == userId)
            .CountAsync();

        var followersCount = await _context.UserFollows
            .Where(f => f.FollowingId == userId)
            .CountAsync();

        return new FollowStatsResponse(followingCount, followersCount);
    }

    public async Task<bool> SetFollowingListVisibilityAsync(ulong userId, bool isHidden)
    {
        var user = await _context.Users.FindAsync(userId);
        if (user == null)
        {
            return false;
        }

        user.IsFollowingListHidden = isHidden;
        await _context.SaveChangesAsync();

        _logger.LogInformation("User {UserId} set following list visibility to {IsHidden}", userId, isHidden);

        return true;
    }

    public async Task<bool> GetFollowingListVisibilityAsync(ulong userId)
    {
        return await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => u.IsFollowingListHidden)
            .FirstOrDefaultAsync();
    }
}
