namespace UserManagementSystem.DTOs;

// ============ 关注相关 DTOs ============

/// <summary>
/// 关注/取消关注响应
/// </summary>
public record FollowResponse(
    ulong UserId,
    string Username,
    string? AvatarUrl,
    bool IsFollowing,
    DateTime FollowedAt
);

/// <summary>
/// 关注列表项
/// </summary>
public record FollowListItem(
    ulong UserId,
    string Username,
    string? AvatarUrl,
    DateTime FollowedAt
);

/// <summary>
/// 关注列表响应
/// </summary>
public record FollowListResponse(
    List<FollowListItem> Items,
    int TotalCount,
    int Page,
    int PageSize
);

/// <summary>
/// 用户关注统计
/// </summary>
public record FollowStatsResponse(
    int FollowingCount,
    int FollowersCount
);
