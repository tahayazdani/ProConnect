using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ProConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification",
                columns: table => new
                {
                    notificationid = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    related_entity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    read_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification", x => x.notificationid);
                    table.ForeignKey(
                        name: "FK_notification_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "userid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_channel_config",
                columns: table => new
                {
                    notificationchannelconfigid = table.Column<Guid>(type: "uuid", nullable: false),
                    notification_type = table.Column<int>(type: "integer", nullable: false),
                    channel = table.Column<int>(type: "integer", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_channel_config", x => x.notificationchannelconfigid);
                });

            migrationBuilder.CreateTable(
                name: "user_device_token",
                columns: table => new
                {
                    userdevicetokenid = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    device_token = table.Column<string>(type: "text", nullable: false),
                    platform = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_seen_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_device_token", x => x.userdevicetokenid);
                    table.ForeignKey(
                        name: "FK_user_device_token_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "userid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "notification_channel_config",
                columns: new[] { "notificationchannelconfigid", "channel", "is_enabled", "notification_type" },
                values: new object[,]
                {
                    { new Guid("55555555-0000-0000-0000-000000000001"), 0, true, 0 },
                    { new Guid("55555555-0000-0000-0000-000000000002"), 1, false, 0 },
                    { new Guid("55555555-0000-0000-0000-000000000003"), 0, true, 1 },
                    { new Guid("55555555-0000-0000-0000-000000000004"), 1, false, 1 },
                    { new Guid("55555555-0000-0000-0000-000000000005"), 0, true, 2 },
                    { new Guid("55555555-0000-0000-0000-000000000006"), 1, false, 2 },
                    { new Guid("55555555-0000-0000-0000-000000000007"), 0, true, 3 },
                    { new Guid("55555555-0000-0000-0000-000000000008"), 1, false, 3 },
                    { new Guid("55555555-0000-0000-0000-000000000009"), 0, true, 4 },
                    { new Guid("55555555-0000-0000-0000-000000000010"), 1, false, 4 },
                    { new Guid("55555555-0000-0000-0000-000000000011"), 0, true, 5 },
                    { new Guid("55555555-0000-0000-0000-000000000012"), 1, false, 5 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_userid_created_at_utc",
                table: "notification",
                columns: new[] { "userid", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_userid_is_read",
                table: "notification",
                columns: new[] { "userid", "is_read" });

            migrationBuilder.CreateIndex(
                name: "IX_notification_channel_config_notification_type_channel",
                table: "notification_channel_config",
                columns: new[] { "notification_type", "channel" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_device_token_device_token",
                table: "user_device_token",
                column: "device_token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_device_token_userid",
                table: "user_device_token",
                column: "userid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification");

            migrationBuilder.DropTable(
                name: "notification_channel_config");

            migrationBuilder.DropTable(
                name: "user_device_token");
        }
    }
}
