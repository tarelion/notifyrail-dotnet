using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "queued"),
                    attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claimed_by = table.Column<string>(type: "text", nullable: true),
                    provider_message_id = table.Column<string>(type: "text", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.id);
                    table.CheckConstraint("deliveries_attempt_count_check", "attempt_count >= 0");
                    table.CheckConstraint("deliveries_claim_check", "(status <> 'processing' AND claimed_at IS NULL AND claimed_by IS NULL) OR (status = 'processing' AND claimed_at IS NOT NULL AND claimed_by IS NOT NULL AND btrim(claimed_by) <> '')");
                    table.CheckConstraint("deliveries_provider_message_id_check", "provider_message_id IS NULL OR btrim(provider_message_id) <> ''");
                    table.CheckConstraint("deliveries_recipient_check", "btrim(recipient) <> ''");
                    table.CheckConstraint("deliveries_retry_schedule_check", "(status = 'retry_scheduled' AND next_attempt_at IS NOT NULL) OR (status <> 'retry_scheduled' AND next_attempt_at IS NULL)");
                    table.CheckConstraint("deliveries_status_check", "status IN ('queued', 'processing', 'sent', 'delivered', 'retry_scheduled', 'failed', 'expired')");
                    table.ForeignKey(
                        name: "deliveries_message_id_fkey",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "deliveries_due_idx",
                table: "deliveries",
                columns: new[] { "status", "next_attempt_at", "created_at" },
                filter: "status IN ('queued', 'retry_scheduled')");

            migrationBuilder.CreateIndex(
                name: "deliveries_message_id_recipient_key",
                table: "deliveries",
                columns: new[] { "message_id", "recipient" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "deliveries_provider_message_id_idx",
                table: "deliveries",
                column: "provider_message_id",
                unique: true,
                filter: "provider_message_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deliveries");
        }
    }
}
