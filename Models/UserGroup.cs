using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

[Table("user_groups")]
public class UserGroup
{
    [Key]
    [Column("id")]
    public ulong Id { get; set; }

    [Required]
    [MaxLength(64)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [MaxLength(256)]
    [Column("description")]
    public string? Description { get; set; }

    /// <summary>
    /// 受保护的用户组不可删除，初始用户组不可编辑任何内容
    /// </summary>
    [Column("is_protected")]
    public bool IsProtected { get; set; }

    /// <summary>
    /// 成员锁定的用户组不可添加或移除成员（仅用于"初始用户"组）
    /// </summary>
    [Column("is_member_locked")]
    public bool IsMemberLocked { get; set; }

    /// <summary>
    /// 「基本用户权限」：为 true 时，新注册用户将默认加入该权限组。
    /// 默认关闭（false）；允许同时有多个组开启。
    /// </summary>
    [Column("is_default_for_new_users")]
    public bool IsDefaultForNewUsers { get; set; }

    /// <summary>
    /// 权限组级别，<strong>数值越小权限越高</strong>（见 <see cref="DTOs.PermissionCodes.GroupLevel"/>）。
    /// 用于判断操作者能否管理某个用户：目标用户的最高级别必须数值大于操作者自己的级别。
    /// </summary>
    [Column("level")]
    public int Level { get; set; } = DTOs.PermissionCodes.GroupLevel.Default;

    public virtual ICollection<UserGroupMember> Members { get; set; } = new List<UserGroupMember>();
    public virtual ICollection<UserGroupPermission> GroupPermissions { get; set; } = new List<UserGroupPermission>();
}

[Table("user_group_members")]
public class UserGroupMember
{
    [Key]
    [Column("id")]
    public ulong Id { get; set; }

    [Column("user_id")]
    public ulong UserId { get; set; }

    [Column("group_id")]
    public ulong GroupId { get; set; }

    [Column("joined_at")]
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("UserId")]
    public virtual User User { get; set; } = null!;

    [ForeignKey("GroupId")]
    public virtual UserGroup Group { get; set; } = null!;
}

[Table("user_group_permissions")]
public class UserGroupPermission
{
    [Key]
    [Column("id")]
    public ulong Id { get; set; }

    [Column("group_id")]
    public ulong GroupId { get; set; }

    [Column("permission_id")]
    public ulong PermissionId { get; set; }

    [Column("granted_at")]
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey("GroupId")]
    public virtual UserGroup Group { get; set; } = null!;

    [ForeignKey("PermissionId")]
    public virtual Permission Permission { get; set; } = null!;
}
