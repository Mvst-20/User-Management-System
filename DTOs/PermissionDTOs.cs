using System.Text.Json;

namespace UserManagementSystem.DTOs;

// ============ 权限请求 DTOs ============

public record CreatePermissionRequest(
    string Code,
    string Name,
    string Category,
    string? Description = null
);

public record UpdatePermissionRequest(
    string? Name = null,
    string? Description = null
);

// ============ 权限响应 DTOs ============

public record PermissionResponse(
    ulong Id,
    string Code,
    string Name,
    string Category,
    bool IsSpecial,
    bool IsSystem,
    string? Description,
    ulong? CreatorId,
    DateTime CreatedAt,

    /// <summary>归属的子系统 ID；为空表示 UCS 自有权限或业务自定义权限。</summary>
    ulong? SubsystemId = null,

    /// <summary>归属的子系统名称（便于界面区分权限来源）。</summary>
    string? SubsystemName = null
);

/// <summary>
/// 子系统权限码自注册同步结果（全量替换语义）。
/// </summary>
public record SubsystemPermissionSyncResult(
    /// <summary>新建的权限数。</summary>
    int Added,

    /// <summary>已存在且归属本子系统、本次更新了名称/描述的权限数。</summary>
    int Updated,

    /// <summary>清单外、原归属本子系统的权限数（随同步被删除，组授权级联清理）。</summary>
    int Deleted,

    /// <summary>冲突/格式非法的权限码（非空时本次同步整体未提交）。</summary>
    List<string> Conflicts
);

// ============ 权限检查 DTOs ============

public record CheckPermissionRequest(
    string? PermissionCode = null,   // 单码模式（兼容旧调用方）

    ulong? UserId = null,

    string[]? Codes = null          // 批量模式（BWB/IHS 等子系统使用）
);

public record CheckPermissionResponse(
    ulong UserId,
    string PermissionCode,
    bool HasPermission
);

public record BulkCheckPermissionResponse(
    ulong UserId,
    List<PermissionCheckItem> Permissions
);

public record PermissionCheckItem(
    string Code,
    bool Granted
);

/// <summary>
/// 全局权限版本号。前端轮询该值，与本地位于 <c>PermissionState.Version</c> 的快照比较，
/// 不一致时才重新拉取权限集合——把「权限变更感知」的成本压到每 30 秒一次极轻请求。
/// </summary>
public record PermissionVersionResponse(long Version);

// ============ 用户组请求 DTOs ============

public record CreateUserGroupRequest(
    string Name,
    string? Description = null,

    /// <summary>权限组级别，数值越小权限越高；留空使用默认值 100。</summary>
    int? Level = null,

    /// <summary>「基本用户权限」：为 true 时，新注册用户将默认加入该权限组。</summary>
    bool IsDefaultForNewUsers = false
);

public record UpdateUserGroupRequest(
    string? Name = null,
    string? Description = null,

    /// <summary>权限组级别，数值越小权限越高；留空表示不修改。</summary>
    int? Level = null,

    /// <summary>「基本用户权限」：留空表示不修改。</summary>
    bool? IsDefaultForNewUsers = null
);

public record AddGroupMemberRequest(
    ulong UserId
);

public record SetUserGroupsRequest(
    List<ulong> GroupIds
);

/// <summary>
/// 批量设置用户所属权限组的结果。
/// </summary>
public enum SetUserGroupsOutcome
{
    /// <summary>设置成功。</summary>
    Success,

    /// <summary>请求中包含不存在的权限组 ID。</summary>
    InvalidGroup,

    /// <summary>请求试图分配"成员锁定"的受保护权限组（如初始用户组）。</summary>
    LockedGroupNotAssignable
}

public record SetGroupPermissionsRequest(
    List<ulong> PermissionIds
);


// ============ 用户组响应 DTOs ============

public record UserGroupResponse(
    ulong Id,
    string Name,
    string? Description,
    bool IsProtected,
    bool IsMemberLocked,

    /// <summary>「基本用户权限」：为 true 时，新注册用户将默认加入该权限组。</summary>
    bool IsDefaultForNewUsers,
    int MemberCount,
    int PermissionCount,

    /// <summary>权限组级别，数值越小权限越高。</summary>
    int Level
);

public record UserGroupDetailResponse(
    ulong Id,
    string Name,
    string? Description,
    bool IsProtected,
    bool IsMemberLocked,

    /// <summary>「基本用户权限」：为 true 时，新注册用户将默认加入该权限组。</summary>
    bool IsDefaultForNewUsers,
    List<GroupMemberResponse> Members,
    List<PermissionResponse> Permissions,

    /// <summary>权限组级别，数值越小权限越高。</summary>
    int Level
);

public record GroupMemberResponse(
    ulong UserId,
    string Username,
    DateTime JoinedAt,

    /// <summary>成员的最高权限级别（数值越小权限越高），用于前端判断能否管理该成员。</summary>
    int PrivilegeLevel
);

