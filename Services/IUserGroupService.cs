using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

/// <summary>
/// 用户组域服务：用户组 CRUD、成员管理、组权限分配。
/// 权限缓存失效通过 IPermissionService 完成。
/// </summary>
public interface IUserGroupService
{
    // ============ 用户组 CRUD ============
    Task<UserGroup?> GetGroupByIdAsync(ulong id);
    Task<UserGroup?> GetGroupByNameAsync(string name);
    Task<(List<UserGroup> Groups, int TotalCount)> GetGroupsPagedAsync(int page, int pageSize);
    Task<UserGroup> CreateGroupAsync(UserGroup group);
    Task<UserGroup?> UpdateGroupAsync(ulong id, string? name, string? description, int? level = null, bool? isDefaultForNewUsers = null);
    Task<bool> DeleteGroupAsync(ulong id);

    /// <summary>
    /// 取所有开启了「基本用户权限」（<see cref="UserGroup.IsDefaultForNewUsers"/>）的权限组，
    /// 注册新用户时用于自动分配。
    /// </summary>
    Task<List<UserGroup>> GetDefaultGroupsForNewUsersAsync();

    // ============ 成员管理 ============
    Task<bool> AddMemberAsync(ulong groupId, ulong userId);
    Task<bool> RemoveMemberAsync(ulong groupId, ulong userId);
    Task<SetUserGroupsOutcome> SetUserGroupsAsync(ulong userId, List<ulong> groupIds);
    Task<List<UserGroupMember>> GetGroupMembersAsync(ulong groupId);
    Task<List<UserGroupResponse>> GetUserGroupSummariesAsync(ulong userId);

    // ============ 权限分配 ============
    Task<bool> SetGroupPermissionsAsync(ulong groupId, List<ulong> permissionIds);
    Task<List<Permission>> GetGroupPermissionsAsync(ulong groupId);

    // ============ 权限级别 ============

    /// <summary>
    /// 取用户的最高权限级别：所属权限组中<strong>数值最小</strong>的 level；
    /// 不属于任何组时返回 <see cref="PermissionCodes.GroupLevel.NoGroup"/>。
    /// </summary>
    Task<int> GetUserPrivilegeLevelAsync(ulong userId);

    /// <summary>批量取用户的最高权限级别（未传入的用户不在结果中）。</summary>
    Task<Dictionary<ulong, int>> GetUserPrivilegeLevelsAsync(IEnumerable<ulong> userIds);

    /// <summary>批量取权限组的级别（用于校验"只能分配级别更低的组"）。</summary>
    Task<Dictionary<ulong, int>> GetGroupLevelsAsync(IEnumerable<ulong> groupIds);
}
