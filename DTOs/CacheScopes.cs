namespace UserManagementSystem.DTOs;

/// <summary>
/// 缓存版本作用域常量。每个作用域对应 <c>cache_versions</c> 表的一行，
/// 写操作 bump 版本号后，所有实例的旧缓存键自然失效。
/// </summary>
public static class CacheScopes
{
    /// <summary>权限归属与授权（权限码、用户组成员、子系统权限）。</summary>
    public const string Permission = "permission";

    /// <summary>用户状态（正常/封禁/未验证/已删除），用于鉴权链路快速判定。</summary>
    public const string UserStatus = "user_status";
}
