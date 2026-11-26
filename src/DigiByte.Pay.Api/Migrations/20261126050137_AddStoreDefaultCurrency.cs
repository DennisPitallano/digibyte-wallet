using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiByte.Pay.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStoreDefaultCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultCurrency",
                table: "Stores",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultCurrency",
                table: "Stores");
        }
    }
}
