using UserManagementSystem.DTOs;
using UserManagementSystem.Models;

namespace UserManagementSystem.Services;

/// <summary>
/// 权限域服务：权限检查（含 ABC 覆盖）、权限 CRUD、种子数据、权限缓存失效。
/// 用户组与成员管理见 IUserGroupService。
/// </summary>
public interface IPermissionService
{
    // ============ 权限检查 ============
    Task<bool> HasPermissionAsync(ulong userId, string permissionCode);
    Task<List<string>> GetUserPermissionCodesAsync(ulong userId);

    /// <summary>
    /// 获取用户"有效权限码"集合：在原始权限码基础上展开 A/B/C 特殊权限的覆盖效果
    /// （A → 系统内全部权限码；B → 追加权限/用户组管理权限；C → 追加服务器管理权限）。
    /// </summary>
    Task<List<string>> GetEffectivePermissionCodesAsync(ulong userId);

    // ============ 权限 CRUD ============
    Task<Permission?> GetPermissionByIdAsync(ulong id);
    Task<Permission?> GetPermissionByCodeAsync(string code);
    Task<List<Permission>> GetAllPermissionsAsync();
    Task<List<Permission>> GetCustomPermissionsAsync();
    Task<Permission> CreatePermissionAsync(Permission permission);
    Task<Permission?> UpdatePermissionAsync(ulong id, string? name, string? description);
    Task<bool> DeletePermissionAsync(ulong id);

    // ============ 权限归属（子系统自注册）============

    /// <summary>
    /// 子系统凭凭证全量同步（自注册）自己的权限码：
    /// <para>
    /// · 权限码强制以「{子系统code}:」为前缀（长度 ≤64、字符白名单 [a-z0-9:_-]），防伪造与抢占；
    /// · 不存在的权限创建并归属本子系统；已归属本子系统的更新名称/描述；
    /// · 已归属其他子系统、或为 UMS 内置/自定义权限（无归属）的码记入冲突；
    /// · 清单外、原归属本子系统的权限被<strong>直接删除</strong>（组授权级联清理，受影响用户缓存失效）。
    /// </para>
    /// 存在冲突时整体拒绝（返回前不落库）。
    /// </summary>
    Task<SubsystemPermissionSyncResult> SyncSubsystemPermissionsAsync(
        ulong subsystemId, string subsystemCode, List<SubsystemPermissionItem>? items);

    // ============ 缓存失效 ============
    Task InvalidateUserPermissionCacheAsync(ulong userId);

    /// <summary>批量失效用户权限缓存（子系统删除/权限删除后调用）。</summary>
    Task InvalidateUsersPermissionCacheAsync(IEnumerable<ulong> userIds);

    // ============ 种子数据 ============
    Task SeedPermissionsAndGroupsAsync();
}
