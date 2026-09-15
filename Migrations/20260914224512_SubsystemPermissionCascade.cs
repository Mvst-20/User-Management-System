using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UserManagementSystem.Migrations
{
    /// <inheritdoc />
    public partial class SubsystemPermissionCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_permissions_subsystems_subsystem_id",
                table: "permissions");

            migrationBuilder.AddForeignKey(
                name: "FK_permissions_subsystems_subsystem_id",
                table: "permissions",
                column: "subsystem_id",
                principalTable: "subsystems",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_permissions_subsystems_subsystem_id",
                table: "permissions");

            migrationBuilder.AddForeignKey(
                name: "FK_permissions_subsystems_subsystem_id",
                table: "permissions",
                column: "subsystem_id",
                principalTable: "subsystems",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
