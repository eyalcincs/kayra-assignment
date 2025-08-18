using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickApi.Migrations
{
    /// <inheritdoc />
    public partial class AddTypeAndQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Quantity",
                table: "Products",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Products",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Price_NonNegative",
                table: "Products",
                sql: "\"Price\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Quantity_NonNegative",
                table: "Products",
                sql: "\"Quantity\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Price_NonNegative",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Quantity_NonNegative",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Quantity",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Products");
        }
    }
}
