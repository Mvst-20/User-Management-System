using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public interface IPermissionService
{
    // ============ 权限检查 ============
    Task<bool> HasPermissionAsync(ulong userId, string permissionCode);
    Task<List<string>> GetUserPermissionCodesAsync(ulong userId);

    // ============ 权限 CRUD ============
    Task<Permission?> GetPermissionByIdAsync(ulong id);
    Task<Permission?> GetPermissionByCodeAsync(string code);
    Task<List<Permission>> GetAllPermissionsAsync();
    Task<List<Permission>> GetCustomPermissionsAsync();
    Task<Permission> CreatePermissionAsync(Permission permission);
    Task<Permission?> UpdatePermissionAsync(ulong id, string? name, string? description);
    Task<bool> DeletePermissionAsync(ulong id);

    // ============ 用户组 CRUD ============
    Task<UserGroup?> GetGroupByIdAsync(ulong id);
    Task<UserGroup?> GetGroupByNameAsync(string name);
    Task<List<UserGroup>> GetAllGroupsAsync();
    Task<(List<UserGroup> Groups, int TotalCount)> GetGroupsPagedAsync(int page, int pageSize);
    Task<UserGroup> CreateGroupAsync(UserGroup group);
    Task<UserGroup?> UpdateGroupAsync(ulong id, string? name, string? description);
    Task<bool> DeleteGroupAsync(ulong id);

    // ============ 成员管理 ============
    Task<bool> AddMemberAsync(ulong groupId, ulong userId);
    Task<bool> RemoveMemberAsync(ulong groupId, ulong userId);
    Task<List<UserGroupMember>> GetGroupMembersAsync(ulong groupId);
    Task<List<UserGroupMember>> GetUserGroupsAsync(ulong userId);
    Task<bool> SetUserGroupsAsync(ulong userId, List<ulong> groupIds);

    // ============ 权限分配 ============
    Task<bool> SetGroupPermissionsAsync(ulong groupId, List<ulong> permissionIds);
    Task<List<Permission>> GetGroupPermissionsAsync(ulong groupId);

    // ============ 用户组 Extras ============
    Task<UserGroup?> UpdateGroupExtrasAsync(ulong id, string? extras);

    // ============ 种子数据 ============
    Task SeedPermissionsAndGroupsAsync();
    Task InvalidateUserPermissionCacheAsync(ulong userId);
}
