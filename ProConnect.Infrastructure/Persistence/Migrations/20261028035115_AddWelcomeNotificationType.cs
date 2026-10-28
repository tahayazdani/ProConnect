using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ProConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWelcomeNotificationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "notification_channel_config",
                columns: new[] { "notificationchannelconfigid", "channel", "is_enabled", "notification_type" },
                values: new object[,]
                {
                    { new Guid("55555555-0000-0000-0000-000000000013"), 0, true, 6 },
                    { new Guid("55555555-0000-0000-0000-000000000014"), 1, true, 6 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "notification_channel_config",
                keyColumn: "notificationchannelconfigid",
                keyValue: new Guid("55555555-0000-0000-0000-000000000013"));

            migrationBuilder.DeleteData(
                table: "notification_channel_config",
                keyColumn: "notificationchannelconfigid",
                keyValue: new Guid("55555555-0000-0000-0000-000000000014"));
        }
    }
}
