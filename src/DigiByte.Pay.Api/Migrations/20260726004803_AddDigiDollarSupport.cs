using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiByte.Pay.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDigiDollarSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DigiDollarEnabled",
                table: "Stores",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "AmountCentsDd",
                table: "Sessions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "Sessions",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "DGB");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DigiDollarEnabled",
                table: "Stores");

            migrationBuilder.DropColumn(
                name: "AmountCentsDd",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "Sessions");
        }
    }
}
