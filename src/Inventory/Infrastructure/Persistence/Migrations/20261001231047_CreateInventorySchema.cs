using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Inventory.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateInventorySchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "inventory");

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "inventory",
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
                name: "inventory_items",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    on_hand = table.Column<int>(type: "integer", nullable: false),
                    reserved = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_items", x => x.id);
                    table.CheckConstraint(
                        "ck_inventory_items_stock_bounds",
                        "reserved >= 0 AND on_hand >= reserved AND on_hand <= 10000000"
                    );
                    table.CheckConstraint("ck_inventory_items_version", "version >= 1");
                }
            );

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "inventory",
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
                name: "stock_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delta = table.Column<int>(type: "integer", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    on_hand_after = table.Column<int>(type: "integer", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<string>(
                        type: "character varying(64)",
                        maxLength: 64,
                        nullable: true
                    ),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_movements", x => x.id);
                    table.CheckConstraint("ck_stock_movements_delta", "delta <> 0");
                    table.CheckConstraint("ck_stock_movements_on_hand_after", "on_hand_after >= 0");
                    table.ForeignKey(
                        name: "fk_stock_movements_inventory_items_inventory_item_id",
                        column: x => x.inventory_item_id,
                        principalSchema: "inventory",
                        principalTable: "inventory_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_processed_at",
                schema: "inventory",
                table: "inbox_messages",
                column: "processed_at"
            );

            migrationBuilder.CreateIndex(
                name: "ux_inventory_items_sku_active",
                schema: "inventory",
                table: "inventory_items",
                column: "sku",
                unique: true,
                filter: "deleted_at IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_aggregate_id",
                schema: "inventory",
                table: "outbox_messages",
                column: "aggregate_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "inventory",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ix_stock_movements_item_created_at",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "inventory_item_id", "created_at" }
            );

            migrationBuilder.CreateIndex(
                name: "ux_stock_movements_item_idempotency_key",
                schema: "inventory",
                table: "stock_movements",
                columns: new[] { "inventory_item_id", "idempotency_key" },
                unique: true,
                filter: "idempotency_key IS NOT NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "inbox_messages", schema: "inventory");

            migrationBuilder.DropTable(name: "outbox_messages", schema: "inventory");

            migrationBuilder.DropTable(name: "stock_movements", schema: "inventory");

            migrationBuilder.DropTable(name: "inventory_items", schema: "inventory");
        }
    }
}
