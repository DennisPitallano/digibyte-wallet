using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiByte.Pay.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Prefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Label = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Merchants",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    DigiIdAddress = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ApiKeyPrefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ApiKeyHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Merchants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MerchantSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TokenPrefix = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerchantSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AddressIndex = table.Column<int>(type: "integer", nullable: false),
                    Address = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AmountSatoshis = table.Column<long>(type: "bigint", nullable: false),
                    FiatCurrency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    FiatAmount = table.Column<decimal>(type: "numeric", nullable: true),
                    DgbPriceAtCreation = table.Column<decimal>(type: "numeric", nullable: true),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Memo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidTxid = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    ReceivedSatoshis = table.Column<long>(type: "bigint", nullable: false),
                    Confirmations = table.Column<int>(type: "integer", nullable: false),
                    RefundTxid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RefundedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RefundNote = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stores",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    MerchantId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Network = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Xpub = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReceiveAddress = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    NextAddressIndex = table.Column<int>(type: "integer", nullable: false),
                    WebhookUrl = table.Column<string>(type: "text", nullable: true),
                    WebhookSecret = table.Column<string>(type: "text", nullable: true),
                    DefaultSessionExpiryMinutes = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDeliveries",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    EventName = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Attempt = table.Column<int>(type: "integer", nullable: false),
                    StatusCode = table.Column<int>(type: "integer", nullable: true),
                    ResponseSnippet = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DurationMs = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDeliveries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_MerchantId",
                table: "ApiKeys",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_Prefix",
                table: "ApiKeys",
                column: "Prefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_ApiKeyPrefix",
                table: "Merchants",
                column: "ApiKeyPrefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Merchants_DigiIdAddress",
                table: "Merchants",
                column: "DigiIdAddress");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantSessions_MerchantId",
                table: "MerchantSessions",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_MerchantSessions_TokenPrefix",
                table: "MerchantSessions",
                column: "TokenPrefix",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Address",
                table: "Sessions",
                column: "Address");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_MerchantId",
                table: "Sessions",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_Status",
                table: "Sessions",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_StoreId",
                table: "Sessions",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Stores_MerchantId",
                table: "Stores",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_StoreId",
                table: "WebhookDeliveries",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_WebhookDeliveries_StoreId_CreatedAt",
                table: "WebhookDeliveries",
                columns: new[] { "StoreId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "Merchants");

            migrationBuilder.DropTable(
                name: "MerchantSessions");

            migrationBuilder.DropTable(
                name: "Sessions");

            migrationBuilder.DropTable(
                name: "Stores");

            migrationBuilder.DropTable(
                name: "WebhookDeliveries");
        }
    }
}
