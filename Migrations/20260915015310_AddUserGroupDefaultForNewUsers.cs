using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddUserGroupDefaultForNewUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_default_for_new_users",
                table: "user_groups",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            // 旧库一次性回填：让内置的"普通用户"组继续充当新注册用户的默认权限组，
            // 保持与升级前"注册即加入普通用户组"的行为一致。
            // 只在本迁移执行时生效，之后管理员取消勾选不会被再次覆盖。
            migrationBuilder.Sql(
                "UPDATE `user_groups` SET `is_default_for_new_users` = 1 WHERE `name` = '普通用户';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_default_for_new_users",
                table: "user_groups");
        }
    }
}
