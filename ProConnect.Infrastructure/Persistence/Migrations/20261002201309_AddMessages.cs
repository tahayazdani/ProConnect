using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    messageid = table.Column<Guid>(type: "uuid", nullable: false),
                    matchid = table.Column<Guid>(type: "uuid", nullable: false),
                    senderuserid = table.Column<Guid>(type: "uuid", nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    read_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.messageid);
                    table.ForeignKey(
                        name: "FK_messages_matches_matchid",
                        column: x => x.matchid,
                        principalTable: "matches",
                        principalColumn: "matchid",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_messages_users_senderuserid",
                        column: x => x.senderuserid,
                        principalTable: "users",
                        principalColumn: "userid",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_messages_matchid_sent_at_utc",
                table: "messages",
                columns: new[] { "matchid", "sent_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_senderuserid",
                table: "messages",
                column: "senderuserid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages");
        }
    }
}
