using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBirthdateAndIndustryGeneratedColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "birthdate",
                table: "users",
                type: "date",
                nullable: true,
                computedColumnSql: "make_date(birth_year, birth_month, birth_day)",
                stored: true);

            migrationBuilder.AddColumn<string>(
                name: "industry_normalized",
                table: "users",
                type: "text",
                nullable: true,
                computedColumnSql: "lower(industry)",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_birthdate",
                table: "users",
                column: "birthdate");

            migrationBuilder.CreateIndex(
                name: "IX_users_industry_normalized",
                table: "users",
                column: "industry_normalized");

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_birth_day_range",
                table: "users",
                sql: "birth_day IS NULL OR (birth_day BETWEEN 1 AND 31)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_users_birth_month_range",
                table: "users",
                sql: "birth_month IS NULL OR (birth_month BETWEEN 1 AND 12)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_birthdate",
                table: "users");

            migrationBuilder.DropIndex(
                name: "IX_users_industry_normalized",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_users_birth_day_range",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "CK_users_birth_month_range",
                table: "users");

            migrationBuilder.DropColumn(
                name: "birthdate",
                table: "users");

            migrationBuilder.DropColumn(
                name: "industry_normalized",
                table: "users");
        }
    }
}
