using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Shipping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateShippingSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "shipping");

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "shipping",
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
                name: "order_references",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_references", x => x.id);
                }
            );

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "shipping",
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
                name: "shipments",
                schema: "shipping",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    carrier = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    tracking_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    estimated_delivery_date = table.Column<DateOnly>(type: "date", nullable: true),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    failed_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                    table.CheckConstraint(
                        "ck_shipments_carrier",
                        "(status IN ('Preparing', 'Cancelled')) = (carrier IS NULL)"
                    );
                    table.CheckConstraint(
                        "ck_shipments_dispatch",
                        "(dispatched_at IS NOT NULL) = (status IN ('InTransit', 'Delivered', 'Failed'))"
                    );
                    table.CheckConstraint(
                        "ck_shipments_status",
                        "status IN ('Preparing', 'InTransit', 'Delivered', 'Failed', 'Cancelled')"
                    );
                    table.CheckConstraint("ck_shipments_version", "version >= 1");
                }
            );

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_processed_at",
                schema: "shipping",
                table: "inbox_messages",
                column: "processed_at"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_aggregate_id",
                schema: "shipping",
                table: "outbox_messages",
                column: "aggregate_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "shipping",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL"
            );

            migrationBuilder.CreateIndex(
                name: "ux_shipments_order",
                schema: "shipping",
                table: "shipments",
                column: "order_id",
                unique: true
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "inbox_messages", schema: "shipping");

            migrationBuilder.DropTable(name: "order_references", schema: "shipping");

            migrationBuilder.DropTable(name: "outbox_messages", schema: "shipping");

            migrationBuilder.DropTable(name: "shipments", schema: "shipping");
        }
    }
}
