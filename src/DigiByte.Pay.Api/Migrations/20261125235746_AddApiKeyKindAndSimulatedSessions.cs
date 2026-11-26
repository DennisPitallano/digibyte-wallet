using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiByte.Pay.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddApiKeyKindAndSimulatedSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Simulated",
                table: "Sessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "ApiKeys",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "merchant");

            migrationBuilder.AddColumn<string>(
                name: "StoreId",
                table: "ApiKeys",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            // One-off: keys labelled "POS…" become Kind=pos, bound to their store when the
            // merchant has exactly one (see ApiKeyKindBackfill for the reasoning).
            migrationBuilder.Sql(DigiByte.Pay.Api.Data.ApiKeyKindBackfill.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Simulated",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ApiKeys");

            migrationBuilder.DropColumn(
                name: "StoreId",
                table: "ApiKeys");
        }
    }
}
