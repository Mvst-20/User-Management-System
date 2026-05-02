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
    /// 自定义 JSON 扩展字段
    /// </summary>
    [Column("extras", TypeName = "json")]
    public string? Extras { get; set; }

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
