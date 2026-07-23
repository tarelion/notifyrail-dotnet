using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetainDeadWebhookEventHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "dead_at",
                table: "webhook_events",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE webhook_events SET dead_at = updated_at WHERE status = 'dead';");

            migrationBuilder.AddCheckConstraint(
                name: "webhook_events_dead_at_check",
                table: "webhook_events",
                sql: "status <> 'dead' OR dead_at IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "webhook_events_dead_at_check",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "dead_at",
                table: "webhook_events");
        }
    }
}
