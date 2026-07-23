using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeadWebhookEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "webhook_events_status_check",
                table: "webhook_events");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "automatic_attempt_deadline_at",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE webhook_events SET status = 'dead' WHERE status = 'failed';");

            migrationBuilder.AddCheckConstraint(
                name: "webhook_events_status_check",
                table: "webhook_events",
                sql: "status IN ('pending', 'processing', 'retry_scheduled', 'succeeded', 'dead')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "webhook_events_status_check",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "automatic_attempt_deadline_at",
                table: "webhook_events");

            migrationBuilder.Sql(
                "UPDATE webhook_events SET status = 'failed' WHERE status = 'dead';");

            migrationBuilder.AddCheckConstraint(
                name: "webhook_events_status_check",
                table: "webhook_events",
                sql: "status IN ('pending', 'processing', 'retry_scheduled', 'succeeded', 'failed')");
        }
    }
}
