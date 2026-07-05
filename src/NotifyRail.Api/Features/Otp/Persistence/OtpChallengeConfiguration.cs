using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NotifyRail.Api.Features.Otp.Persistence;

public sealed class OtpChallengeConfiguration : IEntityTypeConfiguration<OtpChallenge>
{
    public void Configure(EntityTypeBuilder<OtpChallenge> builder)
    {
        builder.ToTable("otp_challenges", table =>
        {
            table.HasCheckConstraint(
                "otp_challenges_recipient_check",
                "btrim(recipient) <> ''");
            table.HasCheckConstraint(
                "otp_challenges_code_hash_check",
                "octet_length(code_hash) = 32");
            table.HasCheckConstraint(
                "otp_challenges_expiry_check",
                "expires_at > created_at");
            table.HasCheckConstraint(
                "otp_challenges_attempts_check",
                "max_attempts > 0 AND failed_attempt_count >= 0 " +
                "AND failed_attempt_count <= max_attempts");
        });

        builder.HasKey(challenge => challenge.Id);

        builder.Property(challenge => challenge.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(challenge => challenge.MessageId)
            .HasColumnName("message_id");

        builder.HasOne(challenge => challenge.Message)
            .WithMany()
            .HasForeignKey(challenge => challenge.MessageId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("otp_challenges_message_id_fkey");

        builder.Property(challenge => challenge.Recipient)
            .HasColumnName("recipient")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(challenge => challenge.CodeHash)
            .HasColumnName("code_hash")
            .HasColumnType("bytea")
            .IsRequired();

        builder.Property(challenge => challenge.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(challenge => challenge.VerifiedAt)
            .HasColumnName("verified_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(challenge => challenge.FailedAttemptCount)
            .HasColumnName("failed_attempt_count")
            .HasDefaultValue(0);

        builder.Property(challenge => challenge.MaxAttempts)
            .HasColumnName("max_attempts");

        builder.Property(challenge => challenge.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(challenge => challenge.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(challenge => challenge.MessageId)
            .IsUnique()
            .HasDatabaseName("otp_challenges_message_id_key");

        builder.HasIndex(challenge => new { challenge.Recipient, challenge.ExpiresAt })
            .HasDatabaseName("otp_challenges_recipient_expiry_idx");
    }
}
