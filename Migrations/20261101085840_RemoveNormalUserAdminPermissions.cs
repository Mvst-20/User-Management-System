using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class RemoveNormalUserAdminPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 普通用户组不应持有 user:read / user:update：
            // 否则任何普通用户都能读取他人资料、封禁他人账号、查看并踢出他人会话。
            // 种子代码已同步移除这两项；本迁移负责清理已存在的旧库授权。
            migrationBuilder.Sql(@"
DELETE gp
FROM `user_group_permissions` gp
JOIN `user_groups` g ON g.`id` = gp.`group_id`
JOIN `permissions` p  ON p.`id` = gp.`permission_id`
WHERE g.`name` = '普通用户'
  AND p.`code` IN ('user:read', 'user:update');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 不回滚：恢复 user:read / user:update 会重新引入越权缺陷。
            // 如确需恢复，应由管理员在「权限组」页面显式重新授予。
        }
    }
}
