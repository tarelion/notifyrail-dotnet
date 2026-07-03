using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NotifyRail.Api.Features.Deliveries.Persistence;

public sealed class DeliveryConfiguration : IEntityTypeConfiguration<Delivery>
{
    public void Configure(EntityTypeBuilder<Delivery> builder)
    {
        builder.ToTable("deliveries", table =>
        {
            table.HasCheckConstraint(
                "deliveries_recipient_check",
                "btrim(recipient) <> ''");
            table.HasCheckConstraint(
                "deliveries_status_check",
                "status IN ('queued', 'processing', 'sent', 'delivered', " +
                "'retry_scheduled', 'failed', 'expired')");
            table.HasCheckConstraint(
                "deliveries_attempt_count_check",
                "attempt_count >= 0");
            table.HasCheckConstraint(
                "deliveries_retry_schedule_check",
                "(status = 'retry_scheduled' AND next_attempt_at IS NOT NULL) " +
                "OR (status <> 'retry_scheduled' AND next_attempt_at IS NULL)");
            table.HasCheckConstraint(
                "deliveries_claim_check",
                "(status <> 'processing' AND claimed_at IS NULL AND claimed_by IS NULL) " +
                "OR (status = 'processing' AND claimed_at IS NOT NULL " +
                "AND claimed_by IS NOT NULL AND btrim(claimed_by) <> '')");
            table.HasCheckConstraint(
                "deliveries_provider_message_id_check",
                "provider_message_id IS NULL OR btrim(provider_message_id) <> ''");
        });

        builder.HasKey(delivery => delivery.Id);

        builder.Property(delivery => delivery.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(delivery => delivery.MessageId)
            .HasColumnName("message_id");

        builder.HasOne(delivery => delivery.Message)
            .WithMany()
            .HasForeignKey(delivery => delivery.MessageId)
            .OnDelete(DeleteBehavior.NoAction)
            .HasConstraintName("deliveries_message_id_fkey");

        builder.Property(delivery => delivery.Recipient)
            .HasColumnName("recipient")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(delivery => delivery.Status)
            .HasColumnName("status")
            .HasColumnType("text")
            .HasDefaultValue("queued")
            .IsRequired();

        builder.Property(delivery => delivery.AttemptCount)
            .HasColumnName("attempt_count")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(delivery => delivery.NextAttemptAt)
            .HasColumnName("next_attempt_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(delivery => delivery.ClaimedAt)
            .HasColumnName("claimed_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(delivery => delivery.ClaimedBy)
            .HasColumnName("claimed_by")
            .HasColumnType("text");

        builder.Property(delivery => delivery.ProviderMessageId)
            .HasColumnName("provider_message_id")
            .HasColumnType("text");

        builder.Property(delivery => delivery.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(delivery => delivery.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");

        builder.Property(delivery => delivery.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");

        builder.HasIndex(delivery => new { delivery.MessageId, delivery.Recipient })
            .IsUnique()
            .HasDatabaseName("deliveries_message_id_recipient_key");

        builder.HasIndex(delivery => delivery.ProviderMessageId)
            .IsUnique()
            .HasFilter("provider_message_id IS NOT NULL")
            .HasDatabaseName("deliveries_provider_message_id_idx");

        builder.HasIndex(delivery => new
            {
                delivery.Status,
                delivery.NextAttemptAt,
                delivery.CreatedAt,
            })
            .HasFilter("status IN ('queued', 'retry_scheduled')")
            .HasDatabaseName("deliveries_due_idx");
    }
}
