using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiByte.Pay.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDdEverywhere : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "BuyerChoiceEnabled",
                table: "Stores",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "DdDonationAddress",
                table: "Stores",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "BuyerChoice",
                table: "Sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "ExtraAmount",
                table: "Sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtraCurrency",
                table: "Sessions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExtraSeenAt",
                table: "Sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtraTxid",
                table: "Sessions",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAt",
                table: "Sessions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "DdChoiceNoticePending",
                table: "Merchants",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "PaySessionOptions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Currency = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    Address = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    AmountSatoshis = table.Column<long>(type: "bigint", nullable: false),
                    AmountCentsDd = table.Column<long>(type: "bigint", nullable: true),
                    UsdRate = table.Column<decimal>(type: "numeric(28,10)", precision: 28, scale: 10, nullable: true),
                    RateAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaySessionOptions", x => new { x.SessionId, x.Currency });
                    table.ForeignKey(
                        name: "FK_PaySessionOptions_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaySessionOptions_Address",
                table: "PaySessionOptions",
                column: "Address");

            // One-time notice (specs/033 FR-030): merchants that already had DigiDollar on when buyer choice ships.
            migrationBuilder.Sql("""
                UPDATE "Merchants" SET "DdChoiceNoticePending" = TRUE
                WHERE "Id" IN (SELECT "MerchantId" FROM "Stores" WHERE "DigiDollarEnabled" = TRUE)
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaySessionOptions");

            migrationBuilder.DropColumn(
                name: "BuyerChoiceEnabled",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdDonationAddress",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "BuyerChoice",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ExtraAmount",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ExtraCurrency",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ExtraSeenAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ExtraTxid",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "DdChoiceNoticePending",
                table: "Merchants");
        }
    }
}
