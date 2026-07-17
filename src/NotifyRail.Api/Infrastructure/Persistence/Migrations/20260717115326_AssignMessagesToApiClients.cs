using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AssignMessagesToApiClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                INSERT INTO api_clients (id, name, is_enabled, created_at, updated_at)
                VALUES (
                    '00000000-0000-0000-0000-000000000001',
                    'Legacy API Client',
                    TRUE,
                    NOW(),
                    NOW())
                ON CONFLICT (id) DO NOTHING;
                """);

            migrationBuilder.DropIndex(
                name: "messages_idempotency_key_key",
                table: "messages");

            migrationBuilder.AddColumn<Guid>(
                name: "api_client_id",
                table: "messages",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000001"));

            migrationBuilder.Sql(
                "ALTER TABLE messages ALTER COLUMN api_client_id DROP DEFAULT;");

            migrationBuilder.CreateIndex(
                name: "messages_api_client_id_idempotency_key_key",
                table: "messages",
                columns: new[] { "api_client_id", "idempotency_key" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_messages_api_clients_api_client_id",
                table: "messages",
                column: "api_client_id",
                principalTable: "api_clients",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_messages_api_clients_api_client_id",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "messages_api_client_id_idempotency_key_key",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "api_client_id",
                table: "messages");

            migrationBuilder.Sql(
                "DELETE FROM api_clients WHERE id = '00000000-0000-0000-0000-000000000001';");

            migrationBuilder.CreateIndex(
                name: "messages_idempotency_key_key",
                table: "messages",
                column: "idempotency_key",
                unique: true);
        }
    }
}
