using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUserIsActive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_is_active",
                table: "users",
                column: "is_active");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_is_active",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "users");
        }
    }
}
