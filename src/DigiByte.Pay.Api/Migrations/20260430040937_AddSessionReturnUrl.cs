using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DigiByte.Pay.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionReturnUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReturnUrl",
                table: "Sessions",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReturnUrl",
                table: "Sessions");
        }
    }
}
