using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductIdempotencyKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "creation_key",
                schema: "catalog",
                table: "products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.AddColumn<string>(
                name: "deletion_key",
                schema: "catalog",
                table: "products",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true
            );

            migrationBuilder.CreateIndex(
                name: "ux_products_creation_key",
                schema: "catalog",
                table: "products",
                column: "creation_key",
                unique: true,
                filter: "creation_key IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ux_products_creation_key", schema: "catalog", table: "products");

            migrationBuilder.DropColumn(name: "creation_key", schema: "catalog", table: "products");

            migrationBuilder.DropColumn(name: "deletion_key", schema: "catalog", table: "products");
        }
    }
}
