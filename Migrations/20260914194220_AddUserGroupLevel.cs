using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddUserGroupLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "extras",
                table: "user_groups");

            migrationBuilder.AddColumn<int>(
                name: "level",
                table: "user_groups",
                type: "int",
                nullable: false,
                defaultValue: 100);

            // 回填预设权限组的级别（数值越小权限越高）：
            // 初始用户 0 / 超级管理员 2 / 管理员 10 / 普通用户 20，其余保持默认 100
            migrationBuilder.Sql("UPDATE user_groups SET level = 0 WHERE name = '初始用户';");
            migrationBuilder.Sql("UPDATE user_groups SET level = 2 WHERE name = '超级管理员';");
            migrationBuilder.Sql("UPDATE user_groups SET level = 10 WHERE name = '管理员';");
            migrationBuilder.Sql("UPDATE user_groups SET level = 20 WHERE name = '普通用户';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "level",
                table: "user_groups");

            migrationBuilder.AddColumn<string>(
                name: "extras",
                table: "user_groups",
                type: "json",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }
    }
}
