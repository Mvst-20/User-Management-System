using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UserManagementSystem.Configuration;
using UserManagementSystem.Data;
using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

public class PermissionService : IPermissionService
{
    private readonly ApplicationDbContext _context;
    private readonly IMemoryCache _cache;
    private readonly ICacheVersionService _cacheVersionService;
    private readonly ILogger<PermissionService> _logger;

    /// <summary>权限缓存 TTL：版本号失效是主路径，TTL 仅作兜底。</summary>
    private readonly TimeSpan _cacheTtl;

    /// <summary>版本号不可用（降级为 0）时的短 TTL，尽快恢复对权限变更的感知。</summary>
    private static readonly TimeSpan DegradedCacheTtl = TimeSpan.FromSeconds(30);

    public PermissionService(
        ApplicationDbContext context,
        IMemoryCache cache,
        ICacheVersionService cacheVersionService,
        ILogger<PermissionService> logger,
        AppConfiguration appConfig)
    {
        _context = context;
        _cache = cache;
        _cacheVersionService = cacheVersionService;
        _logger = logger;
        _cacheTtl = TimeSpan.FromMinutes(Math.Max(1, appConfig.Permissions.CacheTtlMinutes));
    }

    // ============ 权限归属（子系统自注册）============

    /// <summary>权限码长度上限（与 permissions.code 列宽一致）。</summary>
    private const int PermissionCodeMaxLength = 64;

    public async Task<SubsystemPermissionSyncResult> SyncSubsystemPermissionsAsync(
        ulong subsystemId, string subsystemCode, List<SubsystemPermissionItem>? items)
    {
        // 归一化：去空白、去重（重复声明以首条为准）
        var wantedItems = (items ?? new List<SubsystemPermissionItem>())
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Code))
            .GroupBy(item => item.Code.Trim(), StringComparer.Ordinal)
            .Select(group => group.First() with { Code = group.Key })
            .ToList();

        var conflicts = new List<string>();

        // 前缀 + 格式校验：权限码必须以「{子系统code}:」开头，防伪造与抢占他方权限
        var prefix = subsystemCode.Trim() + ":";
        var wanted = new List<string>(wantedItems.Count);
        foreach (var item in wantedItems)
        {
            if (!item.Code.StartsWith(prefix, StringComparison.Ordinal) ||
                item.Code.Length > PermissionCodeMaxLength ||
                item.Code.Any(ch => ch != ':' && ch != '_' && ch != '-'
                    && (ch < 'a' || ch > 'z') && (ch < '0' || ch > '9')))
            {
                conflicts.Add(item.Code);
            }
            else
            {
                wanted.Add(item.Code);
            }
        }

        var owned = await _context.Permissions
            .Where(p => p.SubsystemId == subsystemId)
            .ToListAsync();
        var ownedByCode = owned.ToDictionary(p => p.Code, StringComparer.Ordinal);

        var touchedCodes = wanted.Union(ownedByCode.Keys, StringComparer.Ordinal).ToList();
        var existing = touchedCodes.Count == 0
            ? new List<Permission>()
            : await _context.Permissions
                .Where(p => touchedCodes.Contains(p.Code))
                .ToListAsync();
        var existingByCode = existing.ToDictionary(p => p.Code, StringComparer.Ordinal);

        var added = 0;
        var updated = 0;
        var toDelete = new List<Permission>();

        if (conflicts.Count == 0)
        {
            foreach (var item in wantedItems)
            {
                if (existingByCode.TryGetValue(item.Code, out var permission))
                {
                    // 只允许"不存在"或"已归属本子系统"两种情况，其余（他方归属/UMS 内置/自定义）已在格式
                    // 校验后由归属判断拦截——子系统 code 前缀规则已排除内置码，这里兜底防御
                    if (!permission.SubsystemId.HasValue || permission.SubsystemId.Value != subsystemId)
                    {
                        conflicts.Add(item.Code);
                        continue;
                    }

                    if ((!string.IsNullOrWhiteSpace(item.Name) && item.Name!.Trim() != permission.Name) ||
                        (item.Description is not null && item.Description.Trim() != permission.Description))
                    {
                        if (!string.IsNullOrWhiteSpace(item.Name))
                        {
                            permission.Name = item.Name!.Trim();
                        }

                        permission.Description = item.Description?.Trim();
                        updated++;
                    }
                }
                else
                {
                    _context.Permissions.Add(new Permission
                    {
                        Code = item.Code,
                        Name = string.IsNullOrWhiteSpace(item.Name) ? item.Code : item.Name!.Trim(),
                        Category = subsystemCode,
                        IsSpecial = false,
                        IsSystem = false,
                        Description = item.Description?.Trim() ?? $"由子系统 {subsystemCode} 自注册",
                        SubsystemId = subsystemId,
                        CreatedAt = DateTime.UtcNow
                    });

                    added++;
                }
            }

            // 全量同步语义：清单外、原归属本子系统的权限直接删除（组授权由 FK 级联清理）
            if (conflicts.Count == 0)
            {
                foreach (var permission in owned.Where(p => !wanted.Contains(p.Code, StringComparer.Ordinal)))
                {
                    toDelete.Add(permission);
                }
            }
        }

        // 存在冲突 → 整体拒绝，不落库
        if (conflicts.Count > 0)
        {
            _logger.LogWarning(
                "子系统 {SubsystemCode}({SubsystemId}) 权限自注册被拒绝，冲突码：{Conflicts}",
                subsystemCode, subsystemId, string.Join(", ", conflicts));

            return new SubsystemPermissionSyncResult(0, 0, 0, conflicts);
        }

        if (toDelete.Count > 0)
        {
            _context.Permissions.RemoveRange(toDelete);
        }

        await _context.SaveChangesAsync();

        // 版本号全局失效：不再反查受影响的用户清单（原实现需要联表 + 逐用户清缓存，
        // 成员规模大时是明显的写放大，且在多实例部署下无法覆盖其他实例的缓存）。
        if (added + updated + toDelete.Count > 0)
        {
            await InvalidateAllPermissionCacheAsync();
        }

        _logger.LogInformation(
            "子系统 {SubsystemCode}({SubsystemId}) 权限自注册同步：新增 {Added}、更新 {Updated}、删除 {Deleted}、已递增权限版本号",
            subsystemCode, subsystemId, added, updated, toDelete.Count);

        return new SubsystemPermissionSyncResult(added, updated, toDelete.Count, conflicts);
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
        var version = await _cacheVersionService.GetVersionAsync(CacheScopes.Permission);
        var cacheKey = UserPermissionCacheKey(version, userId);
        var ttl = CacheTtlFor(version);

        if (_cache.TryGetValue(cacheKey, out List<string>? cachedPermissions) && cachedPermissions != null)
            return cachedPermissions;

        var groupIds = await _context.UserGroupMembers
            .Where(m => m.UserId == userId)
            .Select(m => m.GroupId)
            .ToListAsync();

        if (!groupIds.Any())
        {
            // 缓存空结果，避免每次无权限用户请求都查询数据库
            _cache.Set(cacheKey, new List<string>(), ttl);
            return new List<string>();
        }

        var permissionCodes = await _context.UserGroupPermissions
            .Where(gp => groupIds.Contains(gp.GroupId))
            .Select(gp => gp.Permission.Code)
            .Distinct()
            .ToListAsync();

        _cache.Set(cacheKey, permissionCodes, ttl);

        return permissionCodes;
    }

    public async Task<List<string>> GetEffectivePermissionCodesAsync(ulong userId)
    {
        var granted = await GetUserPermissionCodesAsync(userId);
        var effective = new HashSet<string>(granted, StringComparer.Ordinal);

        // A 权限：展开为系统内全部权限码（含后续新增的自定义权限）
        if (granted.Contains(PermissionCodes.PermA))
        {
            var allCodes = await GetAllPermissionCodesAsync();
            effective.UnionWith(allCodes);
            effective.Add(PermissionCodes.PermA);
            return effective.ToList();
        }

        // B 权限：追加权限管理 + 用户组管理类权限
        if (granted.Contains(PermissionCodes.PermB))
            effective.UnionWith(PermissionCodes.BPermissionOverrides);

        // C 权限：追加服务器管理类权限
        if (granted.Contains(PermissionCodes.PermC))
            effective.UnionWith(PermissionCodes.CPermissionOverrides);

        return effective.ToList();
    }

    /// <summary>
    /// 全量权限码（A 权限展开用）。该结果对 A 权限用户是每请求必经项，
    /// 按版本号缓存后避免高频查询整张权限表。
    /// </summary>
    private async Task<List<string>> GetAllPermissionCodesAsync()
    {
        var version = await _cacheVersionService.GetVersionAsync(CacheScopes.Permission);
        var cacheKey = AllPermissionCodesCacheKey(version);
        var ttl = CacheTtlFor(version);

        if (_cache.TryGetValue(cacheKey, out List<string>? cached) && cached != null)
            return cached;

        var allCodes = await _context.Permissions.Select(p => p.Code).ToListAsync();
        _cache.Set(cacheKey, allCodes, ttl);
        return allCodes;
    }

    private static string UserPermissionCacheKey(long version, ulong userId) => $"perm:v{version}:user:{userId}";

    private static string AllPermissionCodesCacheKey(long version) => $"perm:v{version}:all";

    private TimeSpan CacheTtlFor(long version) => version > 0 ? _cacheTtl : DegradedCacheTtl;

    public Task InvalidateAllPermissionCacheAsync()
        => _cacheVersionService.BumpAsync(CacheScopes.Permission);

    public Task InvalidateUserPermissionCacheAsync(ulong userId)
        => _cacheVersionService.BumpAsync(CacheScopes.Permission);

    public Task InvalidateUsersPermissionCacheAsync(IEnumerable<ulong> userIds)
        => _cacheVersionService.BumpAsync(CacheScopes.Permission);

    public Task<long> GetPermissionVersionAsync(CancellationToken ct = default)
        => _cacheVersionService.GetVersionAsync(CacheScopes.Permission, ct);

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
        return await _context.Permissions
            .Include(p => p.Subsystem)
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Id)
            .ToListAsync();
    }

    public async Task<List<Permission>> GetCustomPermissionsAsync()
    {
        // 公共端点使用：必须排除子系统注册的权限（IsSystem=false 的子系统权限
        // 属于子系统私有声明，不应在未认证端点暴露其归属与描述）
        return await _context.Permissions
            .Include(p => p.Subsystem)
            .Where(p => !p.IsSystem && p.SubsystemId == null)
            .OrderBy(p => p.Category)
            .ThenBy(p => p.Id)
            .ToListAsync();
    }

    public async Task<Permission> CreatePermissionAsync(Permission permission)
    {
        permission.CreatedAt = DateTime.UtcNow;
        _context.Permissions.Add(permission);
        await _context.SaveChangesAsync();

        // 新增权限码会改变 A 权限用户的「全量权限码」展开结果
        await InvalidateAllPermissionCacheAsync();
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

        // 原实现遗漏了缓存失效：删除后持有该权限的用户最长要等 TTL 才失去权限
        await InvalidateAllPermissionCacheAsync();
        return true;
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
            PermissionCodes.UserRead, PermissionCodes.UserCreate, PermissionCodes.UserUpdate, PermissionCodes.UserDelete,
            PermissionCodes.UserList,
            PermissionCodes.UserChangePassword, PermissionCodes.UserChangeEmail, PermissionCodes.UserLogout,
            PermissionCodes.UserResetPassword,
            PermissionCodes.SessionRead, PermissionCodes.SessionRevoke, PermissionCodes.LoginHistoryRead,
            PermissionCodes.AvatarUpload, PermissionCodes.AvatarDelete,
            PermissionCodes.AdminStats, PermissionCodes.AdminServer,
            PermissionCodes.PermissionRead, PermissionCodes.PermissionCreate, PermissionCodes.PermissionUpdate, PermissionCodes.PermissionDelete,
            PermissionCodes.UserGroupRead, PermissionCodes.UserGroupCreate, PermissionCodes.UserGroupUpdate, PermissionCodes.UserGroupDelete,
            PermissionCodes.UserGroupManageMembers, PermissionCodes.UserGroupManagePermissions,
            PermissionCodes.SystemSettingRead, PermissionCodes.SystemSettingUpdate,
            PermissionCodes.SmtpRead, PermissionCodes.SmtpUpdate,
            PermissionCodes.SubsystemRead, PermissionCodes.SubsystemCreate,
            PermissionCodes.SubsystemUpdate, PermissionCodes.SubsystemDelete
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
        PermissionCodes.UserCreate => ("创建用户", "user", "管理员直接创建用户（可跳过邮箱验证）"),
        PermissionCodes.UserUpdate => ("更新用户", "user", "更新用户信息"),
        PermissionCodes.UserDelete => ("删除用户", "user", "删除用户"),
        PermissionCodes.UserList => ("用户列表", "user", "查看用户列表"),
        PermissionCodes.UserChangePassword => ("修改密码", "user", "修改用户密码"),
        PermissionCodes.UserChangeEmail => ("修改邮箱", "user", "修改用户邮箱"),
        PermissionCodes.UserLogout => ("登出", "user", "用户登出"),
        PermissionCodes.UserResetPassword => ("重置密码", "user", "重置指定用户的登录密码"),
        PermissionCodes.SessionRead => ("查看登录设备", "security", "查看本人已登录的设备列表"),
        PermissionCodes.SessionRevoke => ("踢出登录设备", "security", "踢出本人指定设备或全部其他设备"),
        PermissionCodes.LoginHistoryRead => ("查看登录历史", "security", "查看本人的登录、刷新、登出与被踢记录"),
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
        PermissionCodes.SystemSettingRead => ("查看系统设置", "system-setting", "查看系统设置信息"),
        PermissionCodes.SystemSettingUpdate => ("更新系统设置", "system-setting", "更新系统设置信息"),
        PermissionCodes.SmtpRead => ("查看邮件配置", "smtp", "查看邮件服务配置"),
        PermissionCodes.SmtpUpdate => ("管理邮件配置", "smtp", "修改邮件服务配置并发送测试邮件"),
        PermissionCodes.SubsystemRead => ("查看子系统", "subsystem", "查看接入的子系统与凭证信息"),
        PermissionCodes.SubsystemCreate => ("创建子系统", "subsystem", "登记子系统并签发访问凭证"),
        PermissionCodes.SubsystemUpdate => ("管理子系统", "subsystem", "更新子系统信息、启停与重置密钥"),
        PermissionCodes.SubsystemDelete => ("删除子系统", "subsystem", "删除子系统并使其凭证失效"),
        _ => (code, "custom", "")
    };

    private async Task SeedGroupsIfMissingAsync()
    {
        var existingNames = (await _context.UserGroups.Select(g => g.Name).ToListAsync()).ToHashSet();

        var seedGroups = new (string Name, string Description, bool IsProtected, bool IsMemberLocked, int Level, bool IsDefaultForNewUsers)[]
        {
            (PermissionCodes.InitialGroupName, "系统初始用户组，仅持有最高权限，不可编辑", true, true, PermissionCodes.GroupLevel.InitialUser, false),
            (PermissionCodes.SuperAdminGroupName, "持有除A权限外的所有权限", true, false, PermissionCodes.GroupLevel.SuperAdmin, false),
            (PermissionCodes.AdminGroupName, "拥有用户管理和基本管理权限", false, false, PermissionCodes.GroupLevel.Admin, false),
            // 「普通用户」开启「基本用户权限」标记，使新注册用户默认加入该组
            (PermissionCodes.NormalGroupName, "基本用户权限", false, false, PermissionCodes.GroupLevel.NormalUser, true)
        };

        var missing = seedGroups.Where(g => !existingNames.Contains(g.Name)).ToList();
        if (missing.Any())
        {
            _logger.LogInformation("Seeding {Count} missing user groups...", missing.Count);

            foreach (var g in missing)
            {
                _context.UserGroups.Add(new UserGroup
                {
                    Name = g.Name,
                    Description = g.Description,
                    IsProtected = g.IsProtected,
                    IsMemberLocked = g.IsMemberLocked,
                    Level = g.Level,
                    IsDefaultForNewUsers = g.IsDefaultForNewUsers
                });
            }

            await _context.SaveChangesAsync();
        }

        // 旧库升级：为已存在、但级别仍不正确的预设权限组回填级别
        var presetLevels = seedGroups.ToDictionary(g => g.Name, g => g.Level);
        var presetNames = seedGroups.Select(g => g.Name).ToArray();
        var existing = await _context.UserGroups
            .Where(g => presetNames.Contains(g.Name))
            .ToListAsync();

        var changed = false;
        foreach (var group in existing)
        {
            var expected = presetLevels[group.Name];
            if (group.Level != expected)
            {
                group.Level = expected;
                changed = true;
            }
        }

        if (changed)
        {
            await _context.SaveChangesAsync();
            _logger.LogInformation("Backfilled privilege levels for preset user groups");
        }
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
                PermissionCodes.UserChangePassword, PermissionCodes.UserChangeEmail, PermissionCodes.UserResetPassword,
                PermissionCodes.AvatarUpload, PermissionCodes.AvatarDelete,
                PermissionCodes.AdminStats, PermissionCodes.UserGroupRead, PermissionCodes.UserLogout,
                PermissionCodes.SessionRead, PermissionCodes.SessionRevoke, PermissionCodes.LoginHistoryRead,
                PermissionCodes.SystemSettingRead, PermissionCodes.SystemSettingUpdate
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
                PermissionCodes.UserLogout, PermissionCodes.AvatarUpload, PermissionCodes.AvatarDelete,
                PermissionCodes.SessionRead, PermissionCodes.SessionRevoke, PermissionCodes.LoginHistoryRead
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
