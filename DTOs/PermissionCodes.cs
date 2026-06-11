namespace UserManagementSystem.DTOs;

/// <summary>
/// 权限代码常量定义，每个 API 端点对应一个权限
/// </summary>
public static class PermissionCodes
{
    // ============ 特殊权限 ============
    public const string PermA = "PERM_A";  // 最高权限，可使用全站所有 API
    public const string PermB = "PERM_B";  // 权限组管理权限，可管理权限表和用户组（除 ABC 外）
    public const string PermC = "PERM_C";  // 服务器配置管理权限，可使用服务器管理 API

    // ============ 用户相关 ============
    public const string UserRead = "user:read";
    public const string UserUpdate = "user:update";
    public const string UserDelete = "user:delete";
    public const string UserList = "user:list";
    public const string UserChangePassword = "user:change-password";
    public const string UserChangeEmail = "user:change-email";
    public const string UserLogout = "user:logout";

    // ============ 头像相关 ============
    public const string AvatarUpload = "avatar:upload";
    public const string AvatarDelete = "avatar:delete";

    // ============ 管理员相关 ============
    public const string AdminStats = "admin:stats";
    public const string AdminServer = "admin:server";

    // ============ 权限管理 ============
    public const string PermissionRead = "permission:read";
    public const string PermissionCreate = "permission:create";
    public const string PermissionUpdate = "permission:update";
    public const string PermissionDelete = "permission:delete";

    // ============ 用户组管理 ============
    public const string UserGroupRead = "user-group:read";
    public const string UserGroupCreate = "user-group:create";
    public const string UserGroupUpdate = "user-group:update";
    public const string UserGroupDelete = "user-group:delete";
    public const string UserGroupManageMembers = "user-group:manage-members";
    public const string UserGroupManagePermissions = "user-group:manage-permissions";

    // ============ 关注相关 ============
    public const string FollowUser = "follow:user";
    public const string FollowRead = "follow:read";
    public const string FollowManage = "follow:manage";

    // ============ 系统设置相关 ============
    public const string SystemSettingRead = "system-setting:read";
    public const string SystemSettingUpdate = "system-setting:update";

    // ============ B 权限覆盖的权限代码（权限管理 + 用户组管理）============
    public static readonly HashSet<string> BPermissionOverrides = new()
    {
        PermissionRead, PermissionCreate, PermissionUpdate, PermissionDelete,
        UserGroupRead, UserGroupCreate, UserGroupUpdate, UserGroupDelete,
        UserGroupManageMembers, UserGroupManagePermissions
    };

    // ============ C 权限覆盖的权限代码 ============
    public static readonly HashSet<string> CPermissionOverrides = new()
    {
        AdminStats, AdminServer
    };

    // ============ 预设用户组名称常量 ============
    public const string InitialGroupName = "初始用户";
    public const string SuperAdminGroupName = "超级管理员";
    public const string AdminGroupName = "管理员";
    public const string NormalGroupName = "普通用户";
}
