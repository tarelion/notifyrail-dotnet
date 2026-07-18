using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManageWebhookEndpoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "webhook_endpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    api_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "text", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_endpoints", x => x.id);
                    table.CheckConstraint("webhook_endpoints_disabled_at_check", "(is_enabled AND disabled_at IS NULL) OR (NOT is_enabled AND disabled_at IS NOT NULL)");
                    table.CheckConstraint("webhook_endpoints_url_check", "btrim(url) <> ''");
                    table.ForeignKey(
                        name: "FK_webhook_endpoints_api_clients_api_client_id",
                        column: x => x.api_client_id,
                        principalTable: "api_clients",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "webhook_secrets",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    api_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    protected_value = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_webhook_secrets", x => x.id);
                    table.CheckConstraint("webhook_secrets_protected_value_check", "octet_length(protected_value) > 0");
                    table.ForeignKey(
                        name: "FK_webhook_secrets_api_clients_api_client_id",
                        column: x => x.api_client_id,
                        principalTable: "api_clients",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "webhook_endpoints_active_api_client_id_key",
                table: "webhook_endpoints",
                column: "api_client_id",
                unique: true,
                filter: "is_enabled");

            migrationBuilder.CreateIndex(
                name: "webhook_endpoints_api_client_created_at_idx",
                table: "webhook_endpoints",
                columns: new[] { "api_client_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "webhook_secrets_api_client_id_key",
                table: "webhook_secrets",
                column: "api_client_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "webhook_endpoints");

            migrationBuilder.DropTable(
                name: "webhook_secrets");
        }
    }
}
