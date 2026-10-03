using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Portfolio.Customers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateCustomersSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(name: "customers");

            migrationBuilder.CreateTable(
                name: "customer_profiles",
                schema: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    contact_email = table.Column<string>(
                        type: "character varying(254)",
                        maxLength: 254,
                        nullable: false
                    ),
                    phone_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    locale = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamptz", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customer_profiles", x => x.id);
                    table.CheckConstraint(
                        "ck_customer_profiles_phone_e164",
                        "phone_number IS NULL OR phone_number ~ '^\\+[1-9][0-9]{7,14}$'"
                    );
                    table.CheckConstraint("ck_customer_profiles_version", "version >= 1");
                }
            );

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "customers",
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
                schema: "customers",
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

            migrationBuilder.CreateIndex(
                name: "ix_inbox_messages_processed_at",
                schema: "customers",
                table: "inbox_messages",
                column: "processed_at"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_aggregate_id",
                schema: "customers",
                table: "outbox_messages",
                column: "aggregate_id"
            );

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_pending",
                schema: "customers",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "customer_profiles", schema: "customers");

            migrationBuilder.DropTable(name: "inbox_messages", schema: "customers");

            migrationBuilder.DropTable(name: "outbox_messages", schema: "customers");
        }
    }
}
