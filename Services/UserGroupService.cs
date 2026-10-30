using Microsoft.EntityFrameworkCore;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

/// <summary>
/// 用户组域服务实现。方法逻辑与原 PermissionService 中对应实现一致（行为保持迁移）。
/// </summary>
public class UserGroupService : IUserGroupService
{
    private readonly ApplicationDbContext _context;
    private readonly IPermissionService _permissionService;

    public UserGroupService(ApplicationDbContext context, IPermissionService permissionService)
    {
        _context = context;
        _permissionService = permissionService;
    }

    // ============ 用户组 CRUD ============

    public async Task<UserGroup?> GetGroupByIdAsync(ulong id)
    {
        return await _context.UserGroups
            .Include(g => g.Members)
            .Include(g => g.GroupPermissions)
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<UserGroup?> GetGroupByNameAsync(string name)
    {
        return await _context.UserGroups
            .FirstOrDefaultAsync(g => g.Name == name);
    }

    public async Task<(List<UserGroup> Groups, int TotalCount)> GetGroupsPagedAsync(int page, int pageSize)
    {
        var totalCount = await _context.UserGroups.CountAsync();
        var groups = await _context.UserGroups
            .Include(g => g.Members)
            .Include(g => g.GroupPermissions)
            .OrderBy(g => g.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (groups, totalCount);
    }

    public async Task<UserGroup> CreateGroupAsync(UserGroup group)
    {
        _context.UserGroups.Add(group);
        await _context.SaveChangesAsync();
        return group;
    }

    public async Task<UserGroup?> UpdateGroupAsync(ulong id, string? name, string? description, int? level = null, bool? isDefaultForNewUsers = null)
    {
        var group = await _context.UserGroups.FindAsync(id);
        if (group == null) return null;

        if (name != null) group.Name = name;
        if (description != null) group.Description = description;
        if (level.HasValue) group.Level = level.Value;
        if (isDefaultForNewUsers.HasValue) group.IsDefaultForNewUsers = isDefaultForNewUsers.Value;

        await _context.SaveChangesAsync();
        return group;
    }

    public async Task<bool> DeleteGroupAsync(ulong id)
    {
        var group = await _context.UserGroups.FindAsync(id);
        if (group == null) return false;

        _context.UserGroups.Remove(group);
        await _context.SaveChangesAsync();

        // 组成员关系随级联删除一并消失，其权限集合随之变化
        await _permissionService.InvalidateAllPermissionCacheAsync();
        return true;
    }

    /// <summary>
    /// 取所有开启了「基本用户权限」的权限组，注册新用户时用于自动分配。
    /// 成员锁定的组（如初始用户组）一律排除，避免向不可增删成员的组写入。
    /// </summary>
    public async Task<List<UserGroup>> GetDefaultGroupsForNewUsersAsync()
    {
        return await _context.UserGroups
            .Where(g => g.IsDefaultForNewUsers && !g.IsMemberLocked)
            .OrderBy(g => g.Id)
            .ToListAsync();
    }

    // ============ 成员管理 ============

    public async Task<bool> AddMemberAsync(ulong groupId, ulong userId)
    {
        var exists = await _context.UserGroupMembers
            .AnyAsync(m => m.GroupId == groupId && m.UserId == userId);

        if (exists) return false;

        var member = new UserGroupMember
        {
            GroupId = groupId,
            UserId = userId,
            JoinedAt = DateTime.UtcNow
        };

        _context.UserGroupMembers.Add(member);
        await _context.SaveChangesAsync();
        await _permissionService.InvalidateUserPermissionCacheAsync(userId);
        return true;
    }

    public async Task<bool> RemoveMemberAsync(ulong groupId, ulong userId)
    {
        // 系统初始用户 (uid=0) 不可从任何用户组移除
        if (userId == 0)
            return false;

        var member = await _context.UserGroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId);

        if (member == null) return false;

        _context.UserGroupMembers.Remove(member);
        await _context.SaveChangesAsync();
        await _permissionService.InvalidateUserPermissionCacheAsync(userId);
        return true;
    }

    public async Task<SetUserGroupsOutcome> SetUserGroupsAsync(ulong userId, List<ulong> groupIds)
    {
        var distinctIds = groupIds.Distinct().ToList();

        var existingGroupIds = await _context.UserGroups
            .Where(g => distinctIds.Contains(g.Id))
            .Select(g => g.Id)
            .ToListAsync();
        if (existingGroupIds.Count != distinctIds.Count)
            return SetUserGroupsOutcome.InvalidGroup;

        var currentMemberships = await _context.UserGroupMembers
            .Where(m => m.UserId == userId)
            .ToListAsync();
        var currentGroupIds = currentMemberships.Select(m => m.GroupId).ToHashSet();

        // 成员锁定的受保护组（"初始用户"组）不允许"新分配"：
        // 该组持有 A 级权限，若可分配给任意用户即为越权提权。
        // 但允许从该组移除成员——这是误加成员的修复路径（uid=0 另有下方保护，不会被移除）。
        var lockedGroupIds = await _context.UserGroups
            .Where(g => g.IsMemberLocked)
            .Select(g => g.Id)
            .ToListAsync();

        if (distinctIds.Any(gid => lockedGroupIds.Contains(gid) && !currentGroupIds.Contains(gid)))
            return SetUserGroupsOutcome.LockedGroupNotAssignable;

        // uid=0 不能被移出初始用户组（防御：即使该组未标记 IsMemberLocked 也保证不丢失）
        if (userId == 0)
        {
            var initialGroup = await GetGroupByNameAsync(PermissionCodes.InitialGroupName);
            if (initialGroup != null && !distinctIds.Contains(initialGroup.Id))
            {
                distinctIds.Add(initialGroup.Id);
            }
        }

        var toRemove = currentMemberships
            .Where(m => !distinctIds.Contains(m.GroupId))
            .ToList();
        if (toRemove.Any())
        {
            _context.UserGroupMembers.RemoveRange(toRemove);
        }

        foreach (var groupId in distinctIds)
        {
            if (!currentGroupIds.Contains(groupId))
            {
                _context.UserGroupMembers.Add(new UserGroupMember
                {
                    GroupId = groupId,
                    UserId = userId,
                    JoinedAt = DateTime.UtcNow
                });
            }
        }

        await _context.SaveChangesAsync();
        await _permissionService.InvalidateUserPermissionCacheAsync(userId);
        return SetUserGroupsOutcome.Success;
    }

    public async Task<List<UserGroupMember>> GetGroupMembersAsync(ulong groupId)
    {
        return await _context.UserGroupMembers
            .Include(m => m.User)
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync();
    }

    // ============ 权限级别 ============

    public async Task<int> GetUserPrivilegeLevelAsync(ulong userId)
    {
        var levels = await _context.UserGroupMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.Group.Level)
            .ToListAsync();

        return levels.Count == 0 ? PermissionCodes.GroupLevel.NoGroup : levels.Min();
    }

    public async Task<Dictionary<ulong, int>> GetUserPrivilegeLevelsAsync(IEnumerable<ulong> userIds)
    {
        var ids = userIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<ulong, int>();
        }

        var levels = await _context.UserGroupMembers
            .Where(m => ids.Contains(m.UserId))
            .GroupBy(m => m.UserId)
            .Select(g => new { UserId = g.Key, Level = g.Min(m => m.Group.Level) })
            .ToListAsync();

        var result = ids.ToDictionary(id => id, _ => PermissionCodes.GroupLevel.NoGroup);
        foreach (var item in levels)
        {
            result[item.UserId] = item.Level;
        }

        return result;
    }

    public async Task<Dictionary<ulong, int>> GetGroupLevelsAsync(IEnumerable<ulong> groupIds)
    {
        var ids = groupIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return new Dictionary<ulong, int>();
        }

        return await _context.UserGroups
            .Where(g => ids.Contains(g.Id))
            .ToDictionaryAsync(g => g.Id, g => g.Level);
    }

    public async Task<List<UserGroupResponse>> GetUserGroupSummariesAsync(ulong userId)
    {
        // 投影计数（EF 翻译为子查询 COUNT），避免加载 Members/GroupPermissions 全集
        return await _context.UserGroupMembers
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.GroupId)
            .Select(m => new UserGroupResponse(
                m.Group.Id,
                m.Group.Name,
                m.Group.Description,
                m.Group.IsProtected,
                m.Group.IsMemberLocked,
                m.Group.IsDefaultForNewUsers,
                m.Group.Members.Count,
                m.Group.GroupPermissions.Count,
                m.Group.Level))
            .ToListAsync();
    }

    // ============ 权限分配 ============

    public async Task<bool> SetGroupPermissionsAsync(ulong groupId, List<ulong> permissionIds)
    {
        var distinctIds = permissionIds.Distinct().ToList();
        var existingIds = await _context.Permissions
            .Where(p => distinctIds.Contains(p.Id))
            .Select(p => p.Id)
            .ToListAsync();

        if (existingIds.Count != distinctIds.Count)
            return false;

        var oldPerms = await _context.UserGroupPermissions
            .Where(gp => gp.GroupId == groupId)
            .ToListAsync();
        _context.UserGroupPermissions.RemoveRange(oldPerms);

        foreach (var permId in distinctIds)
        {
            _context.UserGroupPermissions.Add(new UserGroupPermission
            {
                GroupId = groupId,
                PermissionId = permId,
                GrantedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        // 版本号全局失效：原实现需查出组内全部成员再逐个清缓存，
        // 大组场景是一次明显的写放大，且无法覆盖多实例部署下的其他实例。
        await _permissionService.InvalidateAllPermissionCacheAsync();

        return true;
    }

    public async Task<List<Permission>> GetGroupPermissionsAsync(ulong groupId)
    {
        return await _context.UserGroupPermissions
            .Where(gp => gp.GroupId == groupId)
            .Include(gp => gp.Permission)
            .Select(gp => gp.Permission)
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Id)
            .ToListAsync();
    }

}
