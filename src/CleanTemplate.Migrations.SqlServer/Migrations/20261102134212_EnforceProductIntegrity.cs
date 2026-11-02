using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanTemplate.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class EnforceProductIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [Products] WHERE [Price] < 0 OR [Price] > 9999999999999999.99 OR [Stock] < 0 OR DATALENGTH([Name]) > 400 OR DATALENGTH([Description]) > 2000) THROW 51000, 'Invalid legacy products: review product integrity before migration.', 1;");
            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Price_NonNegative",
                table: "Products",
                sql: "\"Price\" >= 0 AND \"Price\" <= 9999999999999999.99");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_Stock_NonNegative",
                table: "Products",
                sql: "\"Stock\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Price_NonNegative",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Stock_NonNegative",
                table: "Products");
        }
    }
}
