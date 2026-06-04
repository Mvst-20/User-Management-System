using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public class PermissionService : IPermissionService
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PermissionService> _logger;

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public PermissionService(ApplicationDbContext context, IMemoryCache cache, ILogger<PermissionService> logger)
    {
        _context = context;
        _cache = cache;
        _logger = logger;
    }

    // ============ 权限检查 ============

    public async Task<bool> HasPermissionAsync(ulong userId, string permissionCode)
    {
        var permissionCodes = await GetUserPermissionCodesAsync(userId);

        // A 权限：拥有全站所有权限
        if (permissionCodes.Contains(PermissionCodes.PermA))
            return true;

        // B 权限：覆盖权限管理类 + 用户组管理类 API
        if (permissionCodes.Contains(PermissionCodes.PermB) &&
            PermissionCodes.BPermissionOverrides.Contains(permissionCode))
            return true;

        // C 权限：覆盖服务器管理类 API
        if (permissionCodes.Contains(PermissionCodes.PermC) &&
            PermissionCodes.CPermissionOverrides.Contains(permissionCode))
            return true;

        // 精确匹配
        return permissionCodes.Contains(permissionCode);
    }

    public async Task<List<string>> GetUserPermissionCodesAsync(ulong userId)
    {
        var cacheKey = $"user_permissions_{userId}";

        if (_cache.TryGetValue(cacheKey, out List<string>? cachedPermissions) && cachedPermissions != null)
            return cachedPermissions;

        var groupIds = await _context.UserGroupMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.GroupId)
            .ToListAsync();

        if (!groupIds.Any())
            return new List<string>();

        var permissionCodes = await _context.UserGroupPermissions
            .Where(gp => groupIds.Contains(gp.GroupId))
            .Select(gp => gp.Permission.Code)
            .Distinct()
            .ToListAsync();

        _cache.Set(cacheKey, permissionCodes, CacheDuration);

        return permissionCodes;
    }

    public Task InvalidateUserPermissionCacheAsync(ulong userId)
    {
        _cache.Remove($"user_permissions_{userId}");
        return Task.CompletedTask;
    }

    // ============ 权限 CRUD ============

    public async Task<Permission?> GetPermissionByIdAsync(ulong id)
    {
        return await _context.Permissions.FindAsync(id);
    }

    public async Task<Permission?> GetPermissionByCodeAsync(string code)
    {
        return await _context.Permissions.FirstOrDefaultAsync(p => p.Code == code);
    }

    public async Task<List<Permission>> GetAllPermissionsAsync()
    {
        return await _context.Permissions.OrderBy(p => p.Category).ThenBy(p => p.Id).ToListAsync();
    }

    public async Task<List<Permission>> GetCustomPermissionsAsync()
    {
        return await _context.Permissions
            .Where(p => !p.IsSystem)
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Id)
            .ToListAsync();
    }

    public async Task<Permission> CreatePermissionAsync(Permission permission)
    {
        permission.CreatedAt = DateTime.UtcNow;
        _context.Permissions.Add(permission);
        await _context.SaveChangesAsync();
        return permission;
    }

    public async Task<Permission?> UpdatePermissionAsync(ulong id, string? name, string? description)
    {
        var permission = await _context.Permissions.FindAsync(id);
        if (permission == null) return null;

        if (name != null) permission.Name = name;
        if (description != null) permission.Description = description;

        await _context.SaveChangesAsync();
        return permission;
    }

    public async Task<bool> DeletePermissionAsync(ulong id)
    {
        var permission = await _context.Permissions.FindAsync(id);
        if (permission == null) return false;

        _context.Permissions.Remove(permission);
        await _context.SaveChangesAsync();
        return true;
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

    public async Task<List<UserGroup>> GetAllGroupsAsync()
    {
        return await _context.UserGroups
            .Include(g => g.Members)
            .Include(g => g.GroupPermissions)
                .ThenInclude(gp => gp.Permission)
            .OrderBy(g => g.Id)
            .ToListAsync();
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

    public async Task<UserGroup?> UpdateGroupAsync(ulong id, string? name, string? description)
    {
        var group = await _context.UserGroups.FindAsync(id);
        if (group == null) return null;

        if (name != null) group.Name = name;
        if (description != null) group.Description = description;

        await _context.SaveChangesAsync();
        return group;
    }

    public async Task<bool> DeleteGroupAsync(ulong id)
    {
        var group = await _context.UserGroups.FindAsync(id);
        if (group == null) return false;

        _context.UserGroups.Remove(group);
        await _context.SaveChangesAsync();
        return true;
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
        await InvalidateUserPermissionCacheAsync(userId);
        return true;
    }

    public async Task<bool> RemoveMemberAsync(ulong groupId, ulong userId)
    {
        var member = await _context.UserGroupMembers
            .FirstOrDefaultAsync(m => m.GroupId == groupId && m.UserId == userId);

        if (member == null) return false;

        _context.UserGroupMembers.Remove(member);
        await _context.SaveChangesAsync();
        await InvalidateUserPermissionCacheAsync(userId);
        return true;
    }

    public async Task<List<UserGroupMember>> GetGroupMembersAsync(ulong groupId)
    {
        return await _context.UserGroupMembers
            .Include(m => m.User)
            .Where(m => m.GroupId == groupId)
            .OrderBy(m => m.JoinedAt)
            .ToListAsync();
    }

    public async Task<List<UserGroup>> GetUserGroupsAsync(ulong userId)
    {
        return await _context.UserGroupMembers
            .Where(m => m.UserId == userId)
            .Include(m => m.Group)
            .Select(m => m.Group)
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

        // 删除旧权限
        var oldPerms = await _context.UserGroupPermissions
            .Where(gp => gp.GroupId == groupId)
            .ToListAsync();
        _context.UserGroupPermissions.RemoveRange(oldPerms);

        // 添加新权限
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

        // 清除该组所有成员的权限缓存
        var memberIds = await _context.UserGroupMembers
            .Where(m => m.GroupId == groupId)
            .Select(m => m.UserId)
            .ToListAsync();

        foreach (var memberId in memberIds)
        {
            await InvalidateUserPermissionCacheAsync(memberId);
        }

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

    // ============ 用户组 Extras ============

    public async Task<UserGroup?> UpdateGroupExtrasAsync(ulong id, string? extras)
    {
        var group = await _context.UserGroups.FindAsync(id);
        if (group == null) return null;

        group.Extras = extras;
        await _context.SaveChangesAsync();
        return group;
    }

    // ============ 种子数据 ============

    public async Task SeedPermissionsAndGroupsAsync()
    {
        // 逐项幂等检查：权限
        await SeedPermissionsIfMissingAsync();
        // 逐项幂等检查：用户组
        await SeedGroupsIfMissingAsync();
        // 逐项幂等检查：用户组权限
        await SeedGroupPermissionsIfMissingAsync();
    }

    private async Task SeedPermissionsIfMissingAsync()
    {
        var allSeedCodes = new[]
        {
            PermissionCodes.PermA, PermissionCodes.PermB, PermissionCodes.PermC,
            PermissionCodes.UserRead, PermissionCodes.UserUpdate, PermissionCodes.UserDelete, PermissionCodes.UserList,
            PermissionCodes.UserChangePassword, PermissionCodes.UserChangeEmail, PermissionCodes.UserLogout,
            PermissionCodes.AvatarUpload, PermissionCodes.AvatarDelete,
            PermissionCodes.AdminStats, PermissionCodes.AdminServer,
            PermissionCodes.PermissionRead, PermissionCodes.PermissionCreate, PermissionCodes.PermissionUpdate, PermissionCodes.PermissionDelete,
            PermissionCodes.UserGroupRead, PermissionCodes.UserGroupCreate, PermissionCodes.UserGroupUpdate, PermissionCodes.UserGroupDelete,
            PermissionCodes.UserGroupManageMembers, PermissionCodes.UserGroupManagePermissions
        };

        var existingCodes = (await _context.Permissions.Select(p => p.Code).ToListAsync()).ToHashSet();
        var missingCodes = allSeedCodes.Where(c => !existingCodes.Contains(c)).ToList();

        if (!missingCodes.Any()) return;

        _logger.LogInformation("Seeding {Count} missing permissions...", missingCodes.Count);

        var specialCodes = new[] { PermissionCodes.PermA, PermissionCodes.PermB, PermissionCodes.PermC };

        var newPermissions = new List<Permission>();
        foreach (var code in missingCodes)
        {
            var (name, category, description) = GetPermissionMeta(code);
            newPermissions.Add(new Permission
            {
                Code = code,
                Name = name,
                Category = category,
                IsSpecial = specialCodes.Contains(code),
                IsSystem = true,
                Description = description,
                CreatedAt = DateTime.UtcNow
            });
        }

        _context.Permissions.AddRange(newPermissions);
        await _context.SaveChangesAsync();
    }

    private static (string Name, string Category, string Description) GetPermissionMeta(string code) => code switch
    {
        PermissionCodes.PermA => ("最高权限", "special", "拥有全站所有API的访问权限，包括B权限和C权限覆盖的API"),
        PermissionCodes.PermB => ("权限组管理权限", "special", "可以管理权限表和用户组（除ABC权限外的所有权限）"),
        PermissionCodes.PermC => ("服务器配置管理权限", "special", "可以使用服务器管理相关API"),
        PermissionCodes.UserRead => ("查看用户", "user", "查看用户信息"),
        PermissionCodes.UserUpdate => ("更新用户", "user", "更新用户信息"),
        PermissionCodes.UserDelete => ("删除用户", "user", "删除用户"),
        PermissionCodes.UserList => ("用户列表", "user", "查看用户列表"),
        PermissionCodes.UserChangePassword => ("修改密码", "user", "修改用户密码"),
        PermissionCodes.UserChangeEmail => ("修改邮箱", "user", "修改用户邮箱"),
        PermissionCodes.UserLogout => ("登出", "user", "用户登出"),
        PermissionCodes.AvatarUpload => ("上传头像", "avatar", "上传用户头像"),
        PermissionCodes.AvatarDelete => ("删除头像", "avatar", "删除用户头像"),
        PermissionCodes.AdminStats => ("查看统计", "admin", "查看管理员统计数据"),
        PermissionCodes.AdminServer => ("服务器管理", "admin", "查看服务器状态和管理服务器配置"),
        PermissionCodes.PermissionRead => ("查看权限", "permission", "查看权限列表"),
        PermissionCodes.PermissionCreate => ("创建权限", "permission", "创建新权限"),
        PermissionCodes.PermissionUpdate => ("更新权限", "permission", "更新权限信息"),
        PermissionCodes.PermissionDelete => ("删除权限", "permission", "删除权限"),
        PermissionCodes.UserGroupRead => ("查看用户组", "user-group", "查看用户组列表"),
        PermissionCodes.UserGroupCreate => ("创建用户组", "user-group", "创建新用户组"),
        PermissionCodes.UserGroupUpdate => ("更新用户组", "user-group", "更新用户组信息"),
        PermissionCodes.UserGroupDelete => ("删除用户组", "user-group", "删除用户组"),
        PermissionCodes.UserGroupManageMembers => ("管理用户组成员", "user-group", "添加或移除用户组成员"),
        PermissionCodes.UserGroupManagePermissions => ("管理用户组权限", "user-group", "设置用户组权限"),
        _ => (code, "custom", "")
    };

    private async Task SeedGroupsIfMissingAsync()
    {
        var existingNames = (await _context.UserGroups.Select(g => g.Name).ToListAsync()).ToHashSet();

        var seedGroups = new (string Name, string Description, bool IsProtected, bool IsMemberLocked)[]
        {
            (PermissionCodes.InitialGroupName, "系统初始用户组，仅持有最高权限，不可编辑", true, true),
            (PermissionCodes.SuperAdminGroupName, "持有除A权限外的所有权限", true, false),
            (PermissionCodes.AdminGroupName, "拥有用户管理和基本管理权限", false, false),
            (PermissionCodes.NormalGroupName, "基本用户权限", false, false)
        };

        var missing = seedGroups.Where(g => !existingNames.Contains(g.Name)).ToList();
        if (!missing.Any()) return;

        _logger.LogInformation("Seeding {Count} missing user groups...", missing.Count);

        foreach (var g in missing)
        {
            _context.UserGroups.Add(new UserGroup
            {
                Name = g.Name,
                Description = g.Description,
                IsProtected = g.IsProtected,
                IsMemberLocked = g.IsMemberLocked
            });
        }

        await _context.SaveChangesAsync();
    }

    private async Task SeedGroupPermissionsIfMissingAsync()
    {
        var existingAssignments = await _context.UserGroupPermissions
            .Select(gp => new { gp.GroupId, gp.PermissionId })
            .ToListAsync();
        var existingSet = existingAssignments.Select(a => (a.GroupId, a.PermissionId)).ToHashSet();

        var permissions = await _context.Permissions.ToListAsync();
        var permByCode = permissions.ToDictionary(p => p.Code);

        var groups = await _context.UserGroups.ToListAsync();
        var groupByName = groups.ToDictionary(g => g.Name);

        var newAssignments = new List<UserGroupPermission>();

        // 初始用户：仅 A 权限
        if (groupByName.TryGetValue(PermissionCodes.InitialGroupName, out var initialGroup) &&
            permByCode.TryGetValue(PermissionCodes.PermA, out var permA))
        {
            TryAddAssignment(initialGroup.Id, permA.Id);
        }

        // 超级管理员：除 A 外的所有权限
        if (groupByName.TryGetValue(PermissionCodes.SuperAdminGroupName, out var superAdminGroup))
        {
            foreach (var perm in permissions.Where(p => p.Code != PermissionCodes.PermA))
            {
                TryAddAssignment(superAdminGroup.Id, perm.Id);
            }
        }

        // 管理员
        if (groupByName.TryGetValue(PermissionCodes.AdminGroupName, out var adminGroup))
        {
            var adminPermCodes = new[]
            {
                PermissionCodes.UserRead, PermissionCodes.UserUpdate, PermissionCodes.UserDelete, PermissionCodes.UserList,
                PermissionCodes.UserChangePassword, PermissionCodes.UserChangeEmail,
                PermissionCodes.AvatarUpload, PermissionCodes.AvatarDelete,
                PermissionCodes.AdminStats, PermissionCodes.UserGroupRead, PermissionCodes.UserLogout
            };
            foreach (var code in adminPermCodes.Where(c => permByCode.ContainsKey(c)))
            {
                TryAddAssignment(adminGroup.Id, permByCode[code].Id);
            }
        }

        // 普通用户
        if (groupByName.TryGetValue(PermissionCodes.NormalGroupName, out var normalGroup))
        {
            var normalPermCodes = new[]
            {
                PermissionCodes.UserRead, PermissionCodes.UserUpdate,
                PermissionCodes.UserChangePassword, PermissionCodes.UserChangeEmail,
                PermissionCodes.UserLogout, PermissionCodes.AvatarUpload, PermissionCodes.AvatarDelete
            };
            foreach (var code in normalPermCodes.Where(c => permByCode.ContainsKey(c)))
            {
                TryAddAssignment(normalGroup.Id, permByCode[code].Id);
            }
        }

        if (newAssignments.Any())
        {
            _context.UserGroupPermissions.AddRange(newAssignments);
            await _context.SaveChangesAsync();
            _logger.LogInformation("Seeded {Count} missing group-permission assignments", newAssignments.Count);
        }

        void TryAddAssignment(ulong groupId, ulong permissionId)
        {
            if (!existingSet.Contains((groupId, permissionId)))
            {
                newAssignments.Add(new UserGroupPermission
                {
                    GroupId = groupId,
                    PermissionId = permissionId,
                    GrantedAt = DateTime.UtcNow
                });
                existingSet.Add((groupId, permissionId));
            }
        }
    }
}
