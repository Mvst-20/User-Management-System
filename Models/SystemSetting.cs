using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

[Table("system_settings")]
public class SystemSetting
{
    [Key]
    [Column("id")]
    public ulong Id { get; set; }

    /// <summary>
    /// 设置键名（唯一）
    /// </summary>
    [Required]
    [MaxLength(100)]
    [Column("key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// 设置值
    /// </summary>
    [Column("value", TypeName = "text")]
    public string? Value { get; set; }

    /// <summary>
    /// 值类型（string, bool, int, json）
    /// </summary>
    [MaxLength(20)]
    [Column("value_type")]
    public string ValueType { get; set; } = "string";

    /// <summary>
    /// 设置描述
    /// </summary>
    [MaxLength(255)]
    [Column("description")]
    public string? Description { get; set; }

    /// <summary>
    /// 最后更新时间
    /// </summary>
    [Column("updated_at")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新者用户 ID
    /// </summary>
    [Column("updated_by")]
    public ulong? UpdatedBy { get; set; }
}
