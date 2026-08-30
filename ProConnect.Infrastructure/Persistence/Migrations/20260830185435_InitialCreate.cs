using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace ProConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    sub = table.Column<string>(type: "text", nullable: true),
                    emailaddress = table.Column<string>(type: "text", nullable: true),
                    public_identifier = table.Column<string>(type: "text", nullable: true),
                    profile_pic_url = table.Column<string>(type: "text", nullable: true),
                    background_cover_image_url = table.Column<string>(type: "text", nullable: true),
                    first_name = table.Column<string>(type: "text", nullable: true),
                    last_name = table.Column<string>(type: "text", nullable: true),
                    full_name = table.Column<string>(type: "text", nullable: true),
                    follower_count = table.Column<int>(type: "integer", nullable: false),
                    occupation = table.Column<string>(type: "text", nullable: true),
                    headline = table.Column<string>(type: "text", nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    country = table.Column<string>(type: "text", nullable: true),
                    country_full_name = table.Column<string>(type: "text", nullable: true),
                    city = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    location = table.Column<Point>(type: "geography (Point, 4326)", nullable: true),
                    gender = table.Column<string>(type: "text", nullable: true),
                    birth_day = table.Column<int>(type: "integer", nullable: true),
                    birth_month = table.Column<int>(type: "integer", nullable: true),
                    birth_year = table.Column<int>(type: "integer", nullable: true),
                    industry = table.Column<string>(type: "text", nullable: true),
                    company = table.Column<string>(type: "text", nullable: true),
                    interests = table.Column<string[]>(type: "text[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.userid);
                });

            migrationBuilder.CreateTable(
                name: "match_criteria",
                columns: table => new
                {
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    city = table.Column<string>(type: "text", nullable: true),
                    state = table.Column<string>(type: "text", nullable: true),
                    country = table.Column<string>(type: "text", nullable: true),
                    industry = table.Column<string>(type: "text", nullable: true),
                    min_age = table.Column<int>(type: "integer", nullable: true),
                    max_age = table.Column<int>(type: "integer", nullable: true),
                    interests = table.Column<string[]>(type: "text[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_criteria", x => x.userid);
                    table.ForeignKey(
                        name: "FK_match_criteria_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "userid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "matches",
                columns: table => new
                {
                    matchid = table.Column<Guid>(type: "uuid", nullable: false),
                    userid_a = table.Column<Guid>(type: "uuid", nullable: false),
                    userid_b = table.Column<Guid>(type: "uuid", nullable: false),
                    matched_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matches", x => x.matchid);
                    table.ForeignKey(
                        name: "FK_matches_users_userid_a",
                        column: x => x.userid_a,
                        principalTable: "users",
                        principalColumn: "userid",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_matches_users_userid_b",
                        column: x => x.userid_b,
                        principalTable: "users",
                        principalColumn: "userid",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "swipes",
                columns: table => new
                {
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    targetuserid = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<int>(type: "integer", nullable: false),
                    swiped_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_swipes", x => new { x.userid, x.targetuserid });
                    table.ForeignKey(
                        name: "FK_swipes_users_targetuserid",
                        column: x => x.targetuserid,
                        principalTable: "users",
                        principalColumn: "userid",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_swipes_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "userid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_userid_a",
                table: "matches",
                column: "userid_a");

            migrationBuilder.CreateIndex(
                name: "IX_matches_userid_b",
                table: "matches",
                column: "userid_b");

            migrationBuilder.CreateIndex(
                name: "IX_swipes_targetuserid_userid",
                table: "swipes",
                columns: new[] { "targetuserid", "userid" });

            migrationBuilder.CreateIndex(
                name: "IX_users_location",
                table: "users",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "GIST");

            migrationBuilder.CreateIndex(
                name: "IX_users_public_identifier",
                table: "users",
                column: "public_identifier");

            migrationBuilder.CreateIndex(
                name: "IX_users_sub",
                table: "users",
                column: "sub",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_criteria");

            migrationBuilder.DropTable(
                name: "matches");

            migrationBuilder.DropTable(
                name: "swipes");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
