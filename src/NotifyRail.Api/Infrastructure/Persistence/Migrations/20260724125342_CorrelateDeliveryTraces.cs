using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CorrelateDeliveryTraces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source_trace_parent",
                table: "deliveries",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "deliveries_source_trace_parent_check",
                table: "deliveries",
                sql: "source_trace_parent IS NULL OR btrim(source_trace_parent) <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "deliveries_source_trace_parent_check",
                table: "deliveries");

            migrationBuilder.DropColumn(
                name: "source_trace_parent",
                table: "deliveries");
        }
    }
}
