using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace UserManagementSystem.DTOs;

// ============ 权限请求 DTOs ============

public record CreatePermissionRequest(
    [Required(ErrorMessage = "权限代码不能为空")]
    [StringLength(64, ErrorMessage = "权限代码长度不能超过64个字符")]
    string Code,

    [Required(ErrorMessage = "权限名称不能为空")]
    [StringLength(128, ErrorMessage = "权限名称长度不能超过128个字符")]
    string Name,

    [Required(ErrorMessage = "权限分类不能为空")]
    [StringLength(64, ErrorMessage = "权限分类长度不能超过64个字符")]
    string Category,

    [StringLength(256, ErrorMessage = "权限描述长度不能超过256个字符")]
    string? Description = null
);

public record UpdatePermissionRequest(
    [StringLength(128, ErrorMessage = "权限名称长度不能超过128个字符")]
    string? Name = null,

    [StringLength(256, ErrorMessage = "权限描述长度不能超过256个字符")]
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
    DateTime CreatedAt
);

// ============ 权限检查 DTOs ============

public record CheckPermissionRequest(
    [Required(ErrorMessage = "权限代码不能为空")]
    string PermissionCode,

    ulong? UserId = null
);

public record CheckPermissionResponse(
    ulong UserId,
    string PermissionCode,
    bool HasPermission
);

// ============ 用户组请求 DTOs ============

public record CreateUserGroupRequest(
    [Required(ErrorMessage = "用户组名称不能为空")]
    [StringLength(64, ErrorMessage = "用户组名称长度不能超过64个字符")]
    string Name,

    [StringLength(256, ErrorMessage = "用户组描述长度不能超过256个字符")]
    string? Description = null
);

public record UpdateUserGroupRequest(
    [StringLength(64, ErrorMessage = "用户组名称长度不能超过64个字符")]
    string? Name = null,

    [StringLength(256, ErrorMessage = "用户组描述长度不能超过256个字符")]
    string? Description = null
);

public record AddGroupMemberRequest(
    [Required(ErrorMessage = "用户ID不能为空")]
    ulong UserId
);

public record SetUserGroupsRequest(
    [Required(ErrorMessage = "用户组ID列表不能为空")]
    List<ulong> GroupIds
);

public record SetGroupPermissionsRequest(
    [Required(ErrorMessage = "权限ID列表不能为空")]
    List<ulong> PermissionIds
);

public record UpdateUserGroupExtrasRequest(
    JsonElement? Extras = null
);

// ============ 用户组响应 DTOs ============

public record UserGroupResponse(
    ulong Id,
    string Name,
    string? Description,
    bool IsProtected,
    bool IsMemberLocked,
    int MemberCount,
    int PermissionCount
);

public record UserGroupDetailResponse(
    ulong Id,
    string Name,
    string? Description,
    bool IsProtected,
    bool IsMemberLocked,
    List<GroupMemberResponse> Members,
    List<PermissionResponse> Permissions,
    JsonElement? Extras
);

public record GroupMemberResponse(
    ulong UserId,
    string Username,
    DateTime JoinedAt
);

public record UserGroupExtrasResponse(
    ulong Id,
    string Name,
    JsonElement? Extras
);
