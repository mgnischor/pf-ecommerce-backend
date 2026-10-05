using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Cart.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateCartSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "cart");

            migrationBuilder.CreateTable(
                name: "carts",
                schema: "cart",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    currency = table.Column<string>(type: "char(3)", fixedLength: true, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carts", x => x.id);
                    table.CheckConstraint("ck_carts_status", "status IN ('Active', 'CheckedOut', 'Expired')");
                    table.CheckConstraint("ck_carts_version", "version >= 1");
                }
            );

            migrationBuilder.CreateTable(
                name: "catalog_products",
                schema: "cart",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sellable = table.Column<bool>(type: "boolean", nullable: false),
                    price_version = table.Column<int>(type: "integer", nullable: false),
                    status_version = table.Column<int>(type: "integer", nullable: false),
                    price_amount = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    price_currency = table.Column<string>(type: "char(3)", fixedLength: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalog_products", x => x.id);
                    table.CheckConstraint("ck_catalog_products_price_positive", "price_amount > 0");
                    table.CheckConstraint(
                        "ck_catalog_products_source_versions",
                        "price_version >= 1 AND status_version >= 1"
                    );
                    table.CheckConstraint("ck_catalog_products_version", "version >= 1");
                }
            );

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "cart",
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
                schema: "cart",
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
                    trace_parent = table.Column<string>(type: "character varying(55)", maxLength: 55, nullable: true),
                    trace_state = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
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
                name: "cart_items",
                schema: "cart",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cart_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cart_items", x => x.id);
                    table.CheckConstraint("ck_cart_items_quantity", "quantity BETWEEN 1 AND 99");
                    table.ForeignKey(
                        name: "fk_cart_items_carts_cart_id",
                        column: x => x.cart_id,
                        principalSchema: "cart",
                        principalTable: "carts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "ux_cart_items_cart_product",
                schema: "cart",
                table: "cart_items",
                columns: new[] { "cart_id", "product_id" },
                unique: true,
                filter: "deleted_at IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ux_carts_customer_active",
                schema: "cart",
                table: "carts",
                column: "customer_id",
                unique: true,
                filter: "status = 'Active' AND deleted_at IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_processed_at",
                schema: "cart",
                table: "inbox_messages",
                column: "processed_at"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_aggregate_id",
                schema: "cart",
                table: "outbox_messages",
                column: "aggregate_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "cart",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "cart_items", schema: "cart");

            migrationBuilder.DropTable(name: "catalog_products", schema: "cart");

            migrationBuilder.DropTable(name: "inbox_messages", schema: "cart");

            migrationBuilder.DropTable(name: "outbox_messages", schema: "cart");

            migrationBuilder.DropTable(name: "carts", schema: "cart");
        }
    }
}
