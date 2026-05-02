using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

[Table("permissions")]
public class Permission
{
    [Key]
    [Column("id")]
    public ulong Id { get; set; }

    [Required]
    [MaxLength(64)]
    [Column("code")]
    public string Code { get; set; } = string.Empty;

    [Required]
    [MaxLength(128)]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(64)]
    [Column("category")]
    public string Category { get; set; } = string.Empty;

    [Column("is_special")]
    public bool IsSpecial { get; set; }

    /// <summary>
    /// 系统内置权限（种子数据创建），自定义权限为 false
    /// </summary>
    [Column("is_system")]
    public bool IsSystem { get; set; }

    [MaxLength(256)]
    [Column("description")]
    public string? Description { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
