using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorrelateWebhookTraces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_trace_parent",
                table: "webhook_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "webhook_events_source_trace_parent_check",
                table: "webhook_events",
                sql: "source_trace_parent IS NULL OR btrim(source_trace_parent) <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "webhook_events_source_trace_parent_check",
                table: "webhook_events");

            migrationBuilder.DropColumn(
                name: "source_trace_parent",
                table: "webhook_events");
        }
    }
}
