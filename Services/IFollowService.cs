using UserManagementSystem.DTOs;

namespace UserManagementSystem.Services;

public interface IFollowService
{
    /// <summary>
    /// 关注用户
    /// </summary>
    Task<FollowResponse> FollowAsync(ulong followerId, ulong followingId);

    /// <summary>
    /// 取消关注
    /// </summary>
    Task<bool> UnfollowAsync(ulong followerId, ulong followingId);

    /// <summary>
    /// 检查是否已关注
    /// </summary>
    Task<bool> IsFollowingAsync(ulong followerId, ulong followingId);

    /// <summary>
    /// 获取用户的关注列表
    /// </summary>
    Task<FollowListResponse> GetFollowingListAsync(ulong userId, int page, int pageSize, ulong? currentUserId = null);

    /// <summary>
    /// 获取用户的粉丝列表
    /// </summary>
    Task<FollowListResponse> GetFollowersListAsync(ulong userId, int page, int pageSize, ulong? currentUserId = null);

    /// <summary>
    /// 获取关注统计
    /// </summary>
    Task<FollowStatsResponse> GetFollowStatsAsync(ulong userId);

    /// <summary>
    /// 设置关注列表可见性
    /// </summary>
    Task<bool> SetFollowingListVisibilityAsync(ulong userId, bool isHidden);

    /// <summary>
    /// 获取关注列表可见性
    /// </summary>
    Task<bool> GetFollowingListVisibilityAsync(ulong userId);
}
