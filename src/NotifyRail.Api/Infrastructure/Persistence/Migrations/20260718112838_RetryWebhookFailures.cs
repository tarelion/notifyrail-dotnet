using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetryWebhookFailures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "webhook_events_due_idx",
                table: "webhook_events");

            migrationBuilder.DropCheckConstraint(
                name: "webhook_events_status_check",
                table: "webhook_events");

            migrationBuilder.DropCheckConstraint(
                name: "webhook_attempts_outcome_check",
                table: "webhook_attempts");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_attempt_at",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE webhook_attempts SET outcome = 'permanent_failure' WHERE outcome = 'failed';");

            migrationBuilder.CreateIndex(
                name: "webhook_events_due_idx",
                table: "webhook_events",
                columns: new[] { "status", "next_attempt_at", "created_at" });

            migrationBuilder.AddCheckConstraint(
                name: "webhook_events_retry_schedule_check",
                table: "webhook_events",
                sql: "(status = 'retry_scheduled' AND next_attempt_at IS NOT NULL) OR (status <> 'retry_scheduled' AND next_attempt_at IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "webhook_events_status_check",
                table: "webhook_events",
                sql: "status IN ('pending', 'processing', 'retry_scheduled', 'succeeded', 'failed')");

            migrationBuilder.AddCheckConstraint(
                name: "webhook_attempts_outcome_check",
                table: "webhook_attempts",
                sql: "outcome IN ('succeeded', 'retryable_failure', 'permanent_failure')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "webhook_events_due_idx",
                table: "webhook_events");

            migrationBuilder.DropCheckConstraint(
                name: "webhook_events_retry_schedule_check",
                table: "webhook_events");

            migrationBuilder.DropCheckConstraint(
                name: "webhook_events_status_check",
                table: "webhook_events");

            migrationBuilder.DropCheckConstraint(
                name: "webhook_attempts_outcome_check",
                table: "webhook_attempts");

            migrationBuilder.Sql(
                "UPDATE webhook_events SET status = 'failed' WHERE status = 'retry_scheduled';");
            migrationBuilder.Sql(
                "UPDATE webhook_attempts SET outcome = 'failed' " +
                "WHERE outcome IN ('retryable_failure', 'permanent_failure');");

            migrationBuilder.DropColumn(
                name: "next_attempt_at",
                table: "webhook_events");

            migrationBuilder.CreateIndex(
                name: "webhook_events_due_idx",
                table: "webhook_events",
                columns: new[] { "status", "created_at" });

            migrationBuilder.AddCheckConstraint(
                name: "webhook_events_status_check",
                table: "webhook_events",
                sql: "status IN ('pending', 'processing', 'succeeded', 'failed')");

            migrationBuilder.AddCheckConstraint(
                name: "webhook_attempts_outcome_check",
                table: "webhook_attempts",
                sql: "outcome IN ('succeeded', 'failed')");
        }
    }
}
