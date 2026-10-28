using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRefreshTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "refresh_token",
                columns: table => new
                {
                    refreshtokenid = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    tokenhash = table.Column<string>(type: "text", nullable: false),
                    expiresat_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    createdat_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revokedat_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    replacedbytokenhash = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_token", x => x.refreshtokenid);
                    table.ForeignKey(
                        name: "FK_refresh_token_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "userid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_tokenhash",
                table: "refresh_token",
                column: "tokenhash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_token_userid",
                table: "refresh_token",
                column: "userid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "refresh_token");
        }
    }
}
