using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    webhook_event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    http_status_code = table.Column<int>(type: "integer", nullable: true),
                    error_code = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    latency_milliseconds = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_attempts", x => x.id);
                    table.CheckConstraint("webhook_attempts_attempt_number_check", "attempt_number > 0");
                    table.CheckConstraint("webhook_attempts_error_code_check", "error_code IS NULL OR (btrim(error_code) <> '' AND char_length(error_code) <= 100)");
                    table.CheckConstraint("webhook_attempts_error_message_check", "error_message IS NULL OR (btrim(error_message) <> '' AND char_length(error_message) <= 500)");
                    table.CheckConstraint("webhook_attempts_http_status_code_check", "http_status_code IS NULL OR http_status_code BETWEEN 100 AND 599");
                    table.CheckConstraint("webhook_attempts_latency_check", "latency_milliseconds >= 0");
                    table.CheckConstraint("webhook_attempts_outcome_check", "outcome IN ('succeeded', 'failed')");
                    table.CheckConstraint("webhook_attempts_time_check", "completed_at >= attempted_at");
                    table.ForeignKey(
                        name: "FK_webhook_attempts_webhook_events_webhook_event_id",
                        column: x => x.webhook_event_id,
                        principalTable: "webhook_events",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "webhook_attempts_event_id_attempt_number_key",
                table: "webhook_attempts",
                columns: new[] { "webhook_event_id", "attempt_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_attempts");
        }
    }
}
