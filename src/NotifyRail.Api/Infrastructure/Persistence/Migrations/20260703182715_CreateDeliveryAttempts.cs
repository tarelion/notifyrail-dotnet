using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateDeliveryAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delivery_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_number = table.Column<int>(type: "integer", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    outcome = table.Column<string>(type: "text", nullable: false),
                    provider_message_id = table.Column<string>(type: "text", nullable: true),
                    error_code = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    attempted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_attempts", x => x.id);
                    table.CheckConstraint("delivery_attempts_attempt_number_check", "attempt_number > 0");
                    table.CheckConstraint("delivery_attempts_error_code_check", "error_code IS NULL OR btrim(error_code) <> ''");
                    table.CheckConstraint("delivery_attempts_error_message_check", "error_message IS NULL OR btrim(error_message) <> ''");
                    table.CheckConstraint("delivery_attempts_outcome_check", "outcome IN ('accepted', 'retryable_failure', 'permanent_failure')");
                    table.CheckConstraint("delivery_attempts_provider_check", "btrim(provider) <> ''");
                    table.CheckConstraint("delivery_attempts_provider_message_id_check", "provider_message_id IS NULL OR btrim(provider_message_id) <> ''");
                    table.ForeignKey(
                        name: "delivery_attempts_delivery_id_fkey",
                        column: x => x.delivery_id,
                        principalTable: "deliveries",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "delivery_attempts_delivery_id_attempt_number_key",
                table: "delivery_attempts",
                columns: new[] { "delivery_id", "attempt_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_attempts");
        }
    }
}
