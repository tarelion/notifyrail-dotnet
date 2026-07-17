using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandApiClientFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "api_clients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_clients", x => x.id);
                    table.CheckConstraint("api_clients_disabled_at_check", "(is_enabled AND disabled_at IS NULL) OR (NOT is_enabled AND disabled_at IS NOT NULL)");
                    table.CheckConstraint("api_clients_name_check", "btrim(name) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    api_client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lookup_id = table.Column<string>(type: "text", nullable: false),
                    verification_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    display_prefix = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_keys", x => x.id);
                    table.CheckConstraint("api_keys_display_prefix_check", "btrim(display_prefix) <> ''");
                    table.CheckConstraint("api_keys_lookup_id_check", "btrim(lookup_id) <> ''");
                    table.CheckConstraint("api_keys_verification_hash_check", "octet_length(verification_hash) = 32");
                    table.ForeignKey(
                        name: "FK_api_keys_api_clients_api_client_id",
                        column: x => x.api_client_id,
                        principalTable: "api_clients",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "api_keys_lookup_id_key",
                table: "api_keys",
                column: "lookup_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_api_keys_api_client_id",
                table: "api_keys",
                column: "api_client_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "api_clients");
        }
    }
}
