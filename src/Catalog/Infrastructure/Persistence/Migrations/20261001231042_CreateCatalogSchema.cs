using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Catalog.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateCatalogSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "catalog");

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "catalog",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    consumer = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.consumer, x.message_id });
                }
            );

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_version = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    correlation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    causation_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    last_error = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                    table.CheckConstraint("ck_outbox_messages_aggregate_version", "aggregate_version >= 1");
                    table.CheckConstraint("ck_outbox_messages_attempts", "attempts >= 0");
                }
            );

            migrationBuilder.CreateTable(
                name: "products",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(
                        type: "character varying(2000)",
                        maxLength: 2000,
                        nullable: true
                    ),
                    sku = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    price_currency = table.Column<string>(type: "char(3)", fixedLength: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.CheckConstraint("ck_products_name_length", "char_length(name) BETWEEN 3 AND 200");
                    table.CheckConstraint("ck_products_price_positive", "price_amount > 0");
                    table.CheckConstraint("ck_products_status", "status IN ('Draft', 'Active', 'Discontinued')");
                    table.CheckConstraint("ck_products_version", "version >= 1");
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_processed_at",
                schema: "catalog",
                table: "inbox_messages",
                column: "processed_at"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_aggregate_id",
                schema: "catalog",
                table: "outbox_messages",
                column: "aggregate_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "catalog",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ix_products_status_created_at",
                schema: "catalog",
                table: "products",
                columns: new[] { "status", "created_at" },
                filter: "deleted_at IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ux_products_sku_active",
                schema: "catalog",
                table: "products",
                column: "sku",
                unique: true,
                filter: "deleted_at IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "inbox_messages", schema: "catalog");

            migrationBuilder.DropTable(name: "outbox_messages", schema: "catalog");

            migrationBuilder.DropTable(name: "products", schema: "catalog");
        }
    }
}
