using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace UserManagementSystem.Models;

[Table("user_follows")]
public class UserFollow
{
    [Key]
    [Column("id")]
    public ulong Id { get; set; }

    /// <summary>
    /// 关注者用户 ID
    /// </summary>
    [Column("follower_id")]
    public ulong FollowerId { get; set; }

    /// <summary>
    /// 被关注者用户 ID
    /// </summary>
    [Column("following_id")]
    public ulong FollowingId { get; set; }

    /// <summary>
    /// 关注时间
    /// </summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // 导航属性
    public virtual User Follower { get; set; } = null!;
    public virtual User Following { get; set; } = null!;
}
