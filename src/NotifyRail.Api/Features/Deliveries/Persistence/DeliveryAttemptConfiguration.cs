using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NotifyRail.Api.Features.Deliveries.Persistence;

public sealed class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder)
    {
        builder.ToTable("delivery_attempts", table =>
        {
            table.HasCheckConstraint(
                "delivery_attempts_attempt_number_check",
                "attempt_number > 0");
            table.HasCheckConstraint(
                "delivery_attempts_provider_check",
                "btrim(provider) <> ''");
            table.HasCheckConstraint(
                "delivery_attempts_outcome_check",
                "outcome IN ('accepted', 'retryable_failure', 'permanent_failure')");
            table.HasCheckConstraint(
                "delivery_attempts_provider_message_id_check",
                "provider_message_id IS NULL OR btrim(provider_message_id) <> ''");
            table.HasCheckConstraint(
                "delivery_attempts_error_code_check",
                "error_code IS NULL OR btrim(error_code) <> ''");
            table.HasCheckConstraint(
                "delivery_attempts_error_message_check",
                "error_message IS NULL OR btrim(error_message) <> ''");
        });

        builder.HasKey(attempt => attempt.Id);

        builder.Property(attempt => attempt.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(attempt => attempt.DeliveryId)
            .HasColumnName("delivery_id");

        builder.HasOne(attempt => attempt.Delivery)
            .WithMany()
            .HasForeignKey(attempt => attempt.DeliveryId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("delivery_attempts_delivery_id_fkey");

        builder.Property(attempt => attempt.AttemptNumber)
            .HasColumnName("attempt_number")
            .IsRequired();

        builder.Property(attempt => attempt.Provider)
            .HasColumnName("provider")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(attempt => attempt.Outcome)
            .HasColumnName("outcome")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(attempt => attempt.ProviderMessageId)
            .HasColumnName("provider_message_id")
            .HasColumnType("text");

        builder.Property(attempt => attempt.ErrorCode)
            .HasColumnName("error_code")
            .HasColumnType("text");

        builder.Property(attempt => attempt.ErrorMessage)
            .HasColumnName("error_message")
            .HasColumnType("text");

        builder.Property(attempt => attempt.AttemptedAt)
            .HasColumnName("attempted_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");

        builder.HasIndex(attempt => new { attempt.DeliveryId, attempt.AttemptNumber })
            .IsUnique()
            .HasDatabaseName("delivery_attempts_delivery_id_attempt_number_key");
    }
}
