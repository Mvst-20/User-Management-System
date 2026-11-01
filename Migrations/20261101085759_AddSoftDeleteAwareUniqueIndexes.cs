using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteAwareUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_email",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_phone",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_username",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email");

            migrationBuilder.CreateIndex(
                name: "IX_users_phone",
                table: "users",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username");

            // 软删除感知唯一索引：MySQL 不支持部分索引，因此用生成列把 status=4 的行置为 NULL，
            // 再在生成列上建唯一索引（NULL 可重复，软删除记录因此不再占用用户名/邮箱/手机号）。
            migrationBuilder.Sql(@"ALTER TABLE `users`
  ADD COLUMN `username_active` varchar(64)
    GENERATED ALWAYS AS (CASE WHEN `status` = 4 THEN NULL ELSE `username` END) STORED;");

            migrationBuilder.Sql(@"ALTER TABLE `users`
  ADD COLUMN `email_active` varchar(128)
    GENERATED ALWAYS AS (CASE WHEN `status` = 4 THEN NULL ELSE `email` END) STORED;");

            migrationBuilder.Sql(@"ALTER TABLE `users`
  ADD COLUMN `phone_active` varchar(20)
    GENERATED ALWAYS AS (CASE WHEN `status` = 4 THEN NULL ELSE `phone` END) STORED;");

            migrationBuilder.Sql("CREATE UNIQUE INDEX `UX_users_username_active` ON `users` (`username_active`);");
            migrationBuilder.Sql("CREATE UNIQUE INDEX `UX_users_email_active` ON `users` (`email_active`);");
            migrationBuilder.Sql("CREATE UNIQUE INDEX `UX_users_phone_active` ON `users` (`phone_active`);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 回滚前需确认库中不存在“软删除记录与活跃记录重名”，否则重建原唯一索引会失败
            migrationBuilder.Sql("DROP INDEX `UX_users_username_active` ON `users`;");
            migrationBuilder.Sql("DROP INDEX `UX_users_email_active` ON `users`;");
            migrationBuilder.Sql("DROP INDEX `UX_users_phone_active` ON `users`;");

            migrationBuilder.Sql(@"ALTER TABLE `users`
  DROP COLUMN `username_active`,
  DROP COLUMN `email_active`,
  DROP COLUMN `phone_active`;");

            migrationBuilder.DropIndex(
                name: "IX_users_email",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_phone",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_username",
                table: "users");

            migrationBuilder.CreateIndex(
                name: "IX_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_phone",
                table: "users",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_username",
                table: "users",
                column: "username",
                unique: true);
        }
    }
}
