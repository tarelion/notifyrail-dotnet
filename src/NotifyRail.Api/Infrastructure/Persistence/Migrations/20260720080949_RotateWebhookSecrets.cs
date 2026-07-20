using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RotateWebhookSecrets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "webhook_secrets_api_client_id_key",
                table: "webhook_secrets");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retired_at",
                table: "webhook_secrets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "webhook_secrets_active_api_client_id_key",
                table: "webhook_secrets",
                column: "api_client_id",
                unique: true,
                filter: "retired_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "webhook_secrets_api_client_created_at_idx",
                table: "webhook_secrets",
                columns: new[] { "api_client_id", "created_at" });

            migrationBuilder.AddCheckConstraint(
                name: "webhook_secrets_retired_at_check",
                table: "webhook_secrets",
                sql: "retired_at IS NULL OR retired_at >= created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "webhook_secrets_active_api_client_id_key",
                table: "webhook_secrets");

            migrationBuilder.DropIndex(
                name: "webhook_secrets_api_client_created_at_idx",
                table: "webhook_secrets");

            migrationBuilder.DropCheckConstraint(
                name: "webhook_secrets_retired_at_check",
                table: "webhook_secrets");

            migrationBuilder.Sql(
                "DELETE FROM webhook_secrets WHERE retired_at IS NOT NULL");

            migrationBuilder.DropColumn(
                name: "retired_at",
                table: "webhook_secrets");

            migrationBuilder.CreateIndex(
                name: "webhook_secrets_api_client_id_key",
                table: "webhook_secrets",
                column: "api_client_id",
                unique: true);
        }
    }
}
