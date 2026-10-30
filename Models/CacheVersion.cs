using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

/// <summary>
/// 缓存版本号（按作用域）。数据库存储，多实例共享。
/// <para>
/// 权限/用户状态等写操作只需 bump 对应作用域的版本号，各实例的缓存键随之变化，
/// 旧键自然失效——既不需要知道"哪些用户受影响"，也不需要跨实例广播或逐用户清缓存。
/// </para>
/// </summary>
[Table("cache_versions")]
public class CacheVersion
{
    /// <summary>作用域标识（如 permission / user_status），见 <c>CacheScopes</c>。</summary>
    [Key]
    [Column("scope")]
    [MaxLength(64)]
    public string Scope { get; set; } = string.Empty;

    /// <summary>单调递增版本号；0 表示该作用域尚未初始化。</summary>
    [Column("version")]
    public long Version { get; set; }

    /// <summary>最后一次变更时间（UTC）。</summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
