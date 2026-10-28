using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DisableWelcomePush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "notification_channel_config",
                keyColumn: "notificationchannelconfigid",
                keyValue: new Guid("55555555-0000-0000-0000-000000000013"),
                column: "is_enabled",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "notification_channel_config",
                keyColumn: "notificationchannelconfigid",
                keyValue: new Guid("55555555-0000-0000-0000-000000000013"),
                column: "is_enabled",
                value: true);
        }
    }
}
