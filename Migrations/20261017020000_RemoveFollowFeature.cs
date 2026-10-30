using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserManagementSystem.Migrations
{
    /// <summary>
    /// 移除关注/粉丝功能：清理 follow:* 权限数据、删除 user_follows 表与 users.is_following_list_hidden 列。
    /// </summary>
    public partial class RemoveFollowFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. 先清理权限关联（子表），再清理权限（父表）。
            //    PermissionService 的种子逻辑只增不删，旧的 follow:* 权限行必须在这里显式清理。
            migrationBuilder.Sql(
                "DELETE FROM user_group_permissions WHERE permission_id IN (SELECT id FROM permissions WHERE code IN ('follow:user', 'follow:read', 'follow:manage'));");

            migrationBuilder.Sql(
                "DELETE FROM permissions WHERE code IN ('follow:user', 'follow:read', 'follow:manage');");

            // 2. 删除关注关系表
            migrationBuilder.DropTable(
                name: "user_follows");

            // 3. 删除用户表上的关注列表隐私列
            migrationBuilder.DropColumn(
                name: "is_following_list_hidden",
                table: "users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 反向仅恢复表结构；follow:* 权限行不恢复（权限码常量已从代码移除，
            // 种子逻辑不会再写入，如需回退请手动补齐权限数据）。
            migrationBuilder.AddColumn<bool>(
                name: "is_following_list_hidden",
                table: "users",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "user_follows",
                columns: table => new
                {
                    id = table.Column<ulong>(type: "bigint unsigned", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    follower_id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    following_id = table.Column<ulong>(type: "bigint unsigned", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_follows", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_follows_users_follower_id",
                        column: x => x.follower_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_follows_users_following_id",
                        column: x => x.following_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_user_follows_created_at",
                table: "user_follows",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_user_follows_follower_id",
                table: "user_follows",
                column: "follower_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_follows_following_id",
                table: "user_follows",
                column: "following_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_follows_follower_id_following_id",
                table: "user_follows",
                columns: new[] { "follower_id", "following_id" },
                unique: true);
        }
    }
}
