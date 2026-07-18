using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookEventOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    webhook_endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    attempt_count = table.Column<int>(type: "integer", nullable: false),
                    claimed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    claimed_by = table.Column<string>(type: "text", nullable: true),
                    succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_events", x => x.id);
                    table.CheckConstraint("webhook_events_attempt_count_check", "attempt_count >= 0");
                    table.CheckConstraint("webhook_events_claim_check", "(status = 'processing' AND claimed_at IS NOT NULL AND btrim(claimed_by) <> '') OR (status <> 'processing' AND claimed_at IS NULL AND claimed_by IS NULL)");
                    table.CheckConstraint("webhook_events_payload_check", "btrim(payload) <> ''");
                    table.CheckConstraint("webhook_events_sequence_check", "sequence > 0");
                    table.CheckConstraint("webhook_events_status_check", "status IN ('pending', 'processing', 'succeeded', 'failed')");
                    table.CheckConstraint("webhook_events_succeeded_at_check", "(status = 'succeeded' AND succeeded_at IS NOT NULL) OR (status <> 'succeeded' AND succeeded_at IS NULL)");
                    table.CheckConstraint("webhook_events_type_check", "btrim(type) <> ''");
                    table.CheckConstraint("webhook_events_version_check", "version > 0");
                    table.ForeignKey(
                        name: "FK_webhook_events_api_clients_api_client_id",
                        column: x => x.api_client_id,
                        principalTable: "api_clients",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_webhook_events_deliveries_delivery_id",
                        column: x => x.delivery_id,
                        principalTable: "deliveries",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_webhook_events_messages_message_id",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_webhook_events_webhook_endpoints_webhook_endpoint_id",
                        column: x => x.webhook_endpoint_id,
                        principalTable: "webhook_endpoints",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_webhook_events_api_client_id",
                table: "webhook_events",
                column: "api_client_id");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_events_message_id",
                table: "webhook_events",
                column: "message_id");

            migrationBuilder.CreateIndex(
                name: "IX_webhook_events_webhook_endpoint_id",
                table: "webhook_events",
                column: "webhook_endpoint_id");

            migrationBuilder.CreateIndex(
                name: "webhook_events_delivery_id_sequence_key",
                table: "webhook_events",
                columns: new[] { "delivery_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "webhook_events_due_idx",
                table: "webhook_events",
                columns: new[] { "status", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_events");
        }
    }
}
