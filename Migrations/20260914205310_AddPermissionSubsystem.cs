using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class AddPermissionSubsystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(
                name: "subsystem_id",
                table: "permissions",
                type: "bigint unsigned",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_permissions_subsystem_id",
                table: "permissions",
                column: "subsystem_id");

            migrationBuilder.AddForeignKey(
                name: "FK_permissions_subsystems_subsystem_id",
                table: "permissions",
                column: "subsystem_id",
                principalTable: "subsystems",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_permissions_subsystems_subsystem_id",
                table: "permissions");

            migrationBuilder.DropIndex(
                name: "IX_permissions_subsystem_id",
                table: "permissions");

            migrationBuilder.DropColumn(
                name: "subsystem_id",
                table: "permissions");
        }
    }
}
