using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CleanTemplate.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class EnforceProductIntegrity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"Products\" WHERE \"Price\" < 0 OR \"Price\" > 9999999999999999.99 OR \"Stock\" < 0 OR NOT (2 * char_length(\"Name\") - char_length(regexp_replace(\"Name\", U&'[\\+010000-\\+10FFFF]', '', 'g')) <= 200) OR NOT (2 * char_length(\"Description\") - char_length(regexp_replace(\"Description\", U&'[\\+010000-\\+10FFFF]', '', 'g')) <= 1000)) THEN RAISE EXCEPTION 'Invalid legacy products: review product integrity before migration.'; END IF; END $$;");
            migrationBuilder.AddCheckConstraint(name: "CK_Products_Name_Utf16Length", table: "Products", sql: "2 * char_length(\"Name\") - char_length(regexp_replace(\"Name\", U&'[\\+010000-\\+10FFFF]', '', 'g')) <= 200");
            migrationBuilder.AddCheckConstraint(name: "CK_Products_Description_Utf16Length", table: "Products", sql: "2 * char_length(\"Description\") - char_length(regexp_replace(\"Description\", U&'[\\+010000-\\+10FFFF]', '', 'g')) <= 1000");
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
            migrationBuilder.DropCheckConstraint(name: "CK_Products_Name_Utf16Length", table: "Products");
            migrationBuilder.DropCheckConstraint(name: "CK_Products_Description_Utf16Length", table: "Products");
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Price_NonNegative",
                table: "Products");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_Stock_NonNegative",
                table: "Products");
        }
    }
}
