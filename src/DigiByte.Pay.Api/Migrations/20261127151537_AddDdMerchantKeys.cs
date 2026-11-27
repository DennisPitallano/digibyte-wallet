using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiByte.Pay.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDdMerchantKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DdAccountKey",
                table: "Stores",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DdAccountKeySetAt",
                table: "Stores",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DdKeyId",
                table: "Stores",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DdKeySequences",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Network = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    NextIndex = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DdKeySequences", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DdAddresses",
                columns: table => new
                {
                    Address = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DdKeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Index = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StoreId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SessionId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirstPaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DdAddresses", x => x.Address);
                    table.ForeignKey(
                        name: "FK_DdAddresses_DdKeySequences_DdKeyId",
                        column: x => x.DdKeyId,
                        principalTable: "DdKeySequences",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stores_DdKeyId",
                table: "Stores",
                column: "DdKeyId");

            migrationBuilder.CreateIndex(
                name: "IX_DdAddresses_DdKeyId_Index",
                table: "DdAddresses",
                columns: new[] { "DdKeyId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DdAddresses_SessionId",
                table: "DdAddresses",
                column: "SessionId");

            // specs/035: DigiDollar needs the store's own key now. No production store has DD (no node ever ran), so this
            // only turns DD off on dev/testnet stores until a key is added.
            migrationBuilder.Sql("UPDATE \"Stores\" SET \"DigiDollarEnabled\" = FALSE, \"DdDonationAddress\" = NULL WHERE \"DdAccountKey\" IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DdAddresses");

            migrationBuilder.DropTable(
                name: "DdKeySequences");

            migrationBuilder.DropIndex(
                name: "IX_Stores_DdKeyId",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdAccountKey",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdAccountKeySetAt",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "DdKeyId",
                table: "Stores");
        }
    }
}
