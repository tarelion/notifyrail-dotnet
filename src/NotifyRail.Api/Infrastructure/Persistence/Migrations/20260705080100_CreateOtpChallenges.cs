using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NotifyRail.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreateOtpChallenges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "otp_challenges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient = table.Column<string>(type: "text", nullable: false),
                    code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_attempt_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    max_attempts = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_otp_challenges", x => x.id);
                    table.CheckConstraint("otp_challenges_attempts_check", "max_attempts > 0 AND failed_attempt_count >= 0 AND failed_attempt_count <= max_attempts");
                    table.CheckConstraint("otp_challenges_code_hash_check", "octet_length(code_hash) = 32");
                    table.CheckConstraint("otp_challenges_expiry_check", "expires_at > created_at");
                    table.CheckConstraint("otp_challenges_recipient_check", "btrim(recipient) <> ''");
                    table.ForeignKey(
                        name: "otp_challenges_message_id_fkey",
                        column: x => x.message_id,
                        principalTable: "messages",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "otp_challenges_message_id_key",
                table: "otp_challenges",
                column: "message_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "otp_challenges_recipient_expiry_idx",
                table: "otp_challenges",
                columns: new[] { "recipient", "expires_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "otp_challenges");
        }
    }
}
