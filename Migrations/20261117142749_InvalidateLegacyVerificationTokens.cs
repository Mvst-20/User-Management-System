using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class InvalidateLegacyVerificationTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SEC-005：旧版本把邮箱验证 Token 明文存库，本版本改为 SHA-256 哈希存储。
            // 旧 Token 在哈希查询下已不可用；此处一次性清除，避免明文继续留在数据库中。
            // 用户重新触发“重发验证邮件”即可获得新的哈希化 Token。
            migrationBuilder.Sql("DELETE FROM `email_verification_tokens`;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 数据清理不可回滚：旧明文 Token 已失效，不应恢复。
        }
    }
}
