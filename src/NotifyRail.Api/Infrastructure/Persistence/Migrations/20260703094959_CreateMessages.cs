using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    type = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    sender_title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    report_label = table.Column<string>(type: "text", nullable: true),
                    encoding = table.Column<string>(type: "text", nullable: true),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_messages", x => x.id);
                    table.CheckConstraint("messages_body_check", "btrim(body) <> ''");
                    table.CheckConstraint("messages_channel_check", "channel = 'sms'");
                    table.CheckConstraint("messages_encoding_check", "encoding IN ('latin', 'turkish', 'unicode')");
                    table.CheckConstraint("messages_idempotency_key_check", "btrim(idempotency_key) <> ''");
                    table.CheckConstraint("messages_sender_title_check", "btrim(sender_title) <> ''");
                    table.CheckConstraint("messages_type_check", "type IN ('otp', 'transactional', 'campaign')");
                });

            migrationBuilder.CreateIndex(
                name: "messages_idempotency_key_key",
                table: "messages",
                column: "idempotency_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "messages");
        }
    }
}
