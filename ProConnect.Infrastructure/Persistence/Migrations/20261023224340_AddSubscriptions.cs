using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace ProConnect.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "payment_transaction",
                columns: table => new
                {
                    transactionid = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    stripe_event_id = table.Column<string>(type: "text", nullable: true),
                    stripe_session_id = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payment_transaction", x => x.transactionid);
                    table.ForeignKey(
                        name: "FK_payment_transaction_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "userid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "product",
                columns: table => new
                {
                    productid = table.Column<Guid>(type: "uuid", nullable: false),
                    consumable_key = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    quantity_granted = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    stripe_price_id = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_product", x => x.productid);
                });

            migrationBuilder.CreateTable(
                name: "subscription_plan",
                columns: table => new
                {
                    planid = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_code = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    price_monthly = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    price_annual = table.Column<decimal>(type: "numeric(10,2)", nullable: false),
                    stripe_price_id_monthly = table.Column<string>(type: "text", nullable: true),
                    stripe_price_id_annual = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_plan", x => x.planid);
                });

            migrationBuilder.CreateTable(
                name: "user_consumable_balance",
                columns: table => new
                {
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_key = table.Column<string>(type: "text", nullable: false),
                    balance = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_consumable_balance", x => new { x.userid, x.feature_key });
                    table.ForeignKey(
                        name: "FK_user_consumable_balance_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "userid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_feature_usage",
                columns: table => new
                {
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_key = table.Column<string>(type: "text", nullable: false),
                    period_key = table.Column<string>(type: "text", nullable: false),
                    used_count = table.Column<int>(type: "integer", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_feature_usage", x => new { x.userid, x.feature_key, x.period_key });
                    table.ForeignKey(
                        name: "FK_user_feature_usage_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "userid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "plan_feature",
                columns: table => new
                {
                    planfeatureid = table.Column<Guid>(type: "uuid", nullable: false),
                    planid = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_key = table.Column<string>(type: "text", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    limit_value = table.Column<int>(type: "integer", nullable: true),
                    reset_period = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_feature", x => x.planfeatureid);
                    table.ForeignKey(
                        name: "FK_plan_feature_subscription_plan_planid",
                        column: x => x.planid,
                        principalTable: "subscription_plan",
                        principalColumn: "planid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_subscription",
                columns: table => new
                {
                    usersubscriptionid = table.Column<Guid>(type: "uuid", nullable: false),
                    userid = table.Column<Guid>(type: "uuid", nullable: false),
                    planid = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    billing_interval = table.Column<int>(type: "integer", nullable: false),
                    stripe_customer_id = table.Column<string>(type: "text", nullable: true),
                    stripe_subscription_id = table.Column<string>(type: "text", nullable: true),
                    current_period_start_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    current_period_end_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_subscription", x => x.usersubscriptionid);
                    table.ForeignKey(
                        name: "FK_user_subscription_subscription_plan_planid",
                        column: x => x.planid,
                        principalTable: "subscription_plan",
                        principalColumn: "planid",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_subscription_users_userid",
                        column: x => x.userid,
                        principalTable: "users",
                        principalColumn: "userid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "product",
                columns: new[] { "productid", "consumable_key", "currency", "description", "display_name", "is_active", "price", "quantity_granted", "stripe_price_id" },
                values: new object[,]
                {
                    { new Guid("44444444-0000-0000-0000-000000000001"), "rose", "usd", "Stand out in 5 people's queues.", "5 Roses", true, 9.99m, 5, "price_fake_rose_5pack" },
                    { new Guid("44444444-0000-0000-0000-000000000002"), "boost", "usd", "Be a top profile in your area for 30 minutes.", "1 Boost", true, 4.99m, 1, "price_fake_boost_1x" }
                });

            migrationBuilder.InsertData(
                table: "subscription_plan",
                columns: new[] { "planid", "description", "display_name", "is_active", "plan_code", "price_annual", "price_monthly", "sort_order", "stripe_price_id_annual", "stripe_price_id_monthly" },
                values: new object[,]
                {
                    { new Guid("11111111-0000-0000-0000-000000000001"), "Get started for free.", "Free", true, "FREE", 0m, 0m, 1, "price_fake_free_annual", "price_fake_free_monthly" },
                    { new Guid("22222222-0000-0000-0000-000000000002"), "More likes, see who liked you, and search by radius.", "Plus", true, "PLUS", 59.99m, 9.99m, 2, "price_fake_plus_annual", "price_fake_plus_monthly" },
                    { new Guid("33333333-0000-0000-0000-000000000003"), "Unlimited likes and every feature, unlocked.", "Premium", true, "PREMIUM", 119.99m, 19.99m, 3, "price_fake_premium_annual", "price_fake_premium_monthly" }
                });

            migrationBuilder.InsertData(
                table: "plan_feature",
                columns: new[] { "planfeatureid", "feature_key", "is_enabled", "limit_value", "planid", "reset_period" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-0000-0000-000000000001"), "daily_likes", true, 10, new Guid("11111111-0000-0000-0000-000000000001"), 1 },
                    { new Guid("11111111-1111-0000-0000-000000000002"), "who_liked_me_visible", false, null, new Guid("11111111-0000-0000-0000-000000000001"), 0 },
                    { new Guid("11111111-1111-0000-0000-000000000003"), "radius_search_enabled", false, null, new Guid("11111111-0000-0000-0000-000000000001"), 0 },
                    { new Guid("11111111-1111-0000-0000-000000000004"), "read_receipts_enabled", false, null, new Guid("11111111-0000-0000-0000-000000000001"), 0 },
                    { new Guid("11111111-1111-0000-0000-000000000005"), "rewind_enabled", false, null, new Guid("11111111-0000-0000-0000-000000000001"), 0 },
                    { new Guid("11111111-1111-0000-0000-000000000006"), "monthly_roses", false, 0, new Guid("11111111-0000-0000-0000-000000000001"), 2 },
                    { new Guid("22222222-2222-0000-0000-000000000001"), "daily_likes", true, 50, new Guid("22222222-0000-0000-0000-000000000002"), 1 },
                    { new Guid("22222222-2222-0000-0000-000000000002"), "who_liked_me_visible", true, null, new Guid("22222222-0000-0000-0000-000000000002"), 0 },
                    { new Guid("22222222-2222-0000-0000-000000000003"), "radius_search_enabled", true, null, new Guid("22222222-0000-0000-0000-000000000002"), 0 },
                    { new Guid("22222222-2222-0000-0000-000000000004"), "read_receipts_enabled", false, null, new Guid("22222222-0000-0000-0000-000000000002"), 0 },
                    { new Guid("22222222-2222-0000-0000-000000000005"), "rewind_enabled", true, null, new Guid("22222222-0000-0000-0000-000000000002"), 0 },
                    { new Guid("22222222-2222-0000-0000-000000000006"), "monthly_roses", true, 1, new Guid("22222222-0000-0000-0000-000000000002"), 2 },
                    { new Guid("33333333-3333-0000-0000-000000000001"), "daily_likes", true, null, new Guid("33333333-0000-0000-0000-000000000003"), 1 },
                    { new Guid("33333333-3333-0000-0000-000000000002"), "who_liked_me_visible", true, null, new Guid("33333333-0000-0000-0000-000000000003"), 0 },
                    { new Guid("33333333-3333-0000-0000-000000000003"), "radius_search_enabled", true, null, new Guid("33333333-0000-0000-0000-000000000003"), 0 },
                    { new Guid("33333333-3333-0000-0000-000000000004"), "read_receipts_enabled", true, null, new Guid("33333333-0000-0000-0000-000000000003"), 0 },
                    { new Guid("33333333-3333-0000-0000-000000000005"), "rewind_enabled", true, null, new Guid("33333333-0000-0000-0000-000000000003"), 0 },
                    { new Guid("33333333-3333-0000-0000-000000000006"), "monthly_roses", true, 5, new Guid("33333333-0000-0000-0000-000000000003"), 2 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_payment_transaction_stripe_event_id",
                table: "payment_transaction",
                column: "stripe_event_id",
                unique: true,
                filter: "stripe_event_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_payment_transaction_userid_created_at_utc",
                table: "payment_transaction",
                columns: new[] { "userid", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_plan_feature_planid_feature_key",
                table: "plan_feature",
                columns: new[] { "planid", "feature_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_product_consumable_key",
                table: "product",
                column: "consumable_key");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_plan_plan_code",
                table: "subscription_plan",
                column: "plan_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_subscription_planid",
                table: "user_subscription",
                column: "planid");

            migrationBuilder.CreateIndex(
                name: "IX_user_subscription_stripe_customer_id",
                table: "user_subscription",
                column: "stripe_customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_subscription_stripe_subscription_id",
                table: "user_subscription",
                column: "stripe_subscription_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_subscription_userid",
                table: "user_subscription",
                column: "userid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payment_transaction");

            migrationBuilder.DropTable(
                name: "plan_feature");

            migrationBuilder.DropTable(
                name: "product");

            migrationBuilder.DropTable(
                name: "user_consumable_balance");

            migrationBuilder.DropTable(
                name: "user_feature_usage");

            migrationBuilder.DropTable(
                name: "user_subscription");

            migrationBuilder.DropTable(
                name: "subscription_plan");
        }
    }
}
