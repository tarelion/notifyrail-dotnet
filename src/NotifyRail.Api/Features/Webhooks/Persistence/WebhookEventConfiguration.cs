using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotifyRail.Api.Features.ApiClients.Persistence;
using NotifyRail.Api.Features.Deliveries.Persistence;
using NotifyRail.Api.Features.Messages.Persistence;

namespace NotifyRail.Api.Features.Webhooks.Persistence;

public sealed class WebhookEventConfiguration : IEntityTypeConfiguration<WebhookEvent>
{
    public void Configure(EntityTypeBuilder<WebhookEvent> builder)
    {
        builder.ToTable("webhook_events", table =>
        {
            table.HasCheckConstraint("webhook_events_type_check", "btrim(type) <> ''");
            table.HasCheckConstraint("webhook_events_version_check", "version > 0");
            table.HasCheckConstraint("webhook_events_sequence_check", "sequence > 0");
            table.HasCheckConstraint("webhook_events_payload_check", "btrim(payload) <> ''");
            table.HasCheckConstraint(
                "webhook_events_status_check",
                "status IN ('pending', 'processing', 'retry_scheduled', 'succeeded', 'dead')");
            table.HasCheckConstraint("webhook_events_attempt_count_check", "attempt_count >= 0");
            table.HasCheckConstraint(
                "webhook_events_retry_schedule_check",
                "(status = 'retry_scheduled' AND next_attempt_at IS NOT NULL) " +
                "OR (status <> 'retry_scheduled' AND next_attempt_at IS NULL)");
            table.HasCheckConstraint(
                "webhook_events_claim_check",
                "(status = 'processing' AND claimed_at IS NOT NULL AND btrim(claimed_by) <> '') " +
                "OR (status <> 'processing' AND claimed_at IS NULL AND claimed_by IS NULL)");
            table.HasCheckConstraint(
                "webhook_events_succeeded_at_check",
                "(status = 'succeeded' AND succeeded_at IS NOT NULL) " +
                "OR (status <> 'succeeded' AND succeeded_at IS NULL)");
        });

        builder.HasKey(webhookEvent => webhookEvent.Id);
        builder.Property(webhookEvent => webhookEvent.Id).HasColumnName("id");
        builder.Property(webhookEvent => webhookEvent.ApiClientId).HasColumnName("api_client_id");
        builder.Property(webhookEvent => webhookEvent.WebhookEndpointId).HasColumnName("webhook_endpoint_id");
        builder.Property(webhookEvent => webhookEvent.MessageId).HasColumnName("message_id");
        builder.Property(webhookEvent => webhookEvent.DeliveryId).HasColumnName("delivery_id");
        builder.Property(webhookEvent => webhookEvent.Type).HasColumnName("type").HasColumnType("text");
        builder.Property(webhookEvent => webhookEvent.Version).HasColumnName("version");
        builder.Property(webhookEvent => webhookEvent.Sequence).HasColumnName("sequence");
        builder.Property(webhookEvent => webhookEvent.OccurredAt)
            .HasColumnName("occurred_at").HasColumnType("timestamp with time zone");
        builder.Property(webhookEvent => webhookEvent.Payload)
            .HasColumnName("payload").HasColumnType("text");
        builder.Property(webhookEvent => webhookEvent.Status)
            .HasColumnName("status").HasColumnType("text");
        builder.Property(webhookEvent => webhookEvent.AttemptCount).HasColumnName("attempt_count");
        builder.Property(webhookEvent => webhookEvent.NextAttemptAt)
            .HasColumnName("next_attempt_at").HasColumnType("timestamp with time zone");
        builder.Property(webhookEvent => webhookEvent.AutomaticAttemptDeadlineAt)
            .HasColumnName("automatic_attempt_deadline_at").HasColumnType("timestamp with time zone");
        builder.Property(webhookEvent => webhookEvent.ClaimedAt)
            .HasColumnName("claimed_at").HasColumnType("timestamp with time zone");
        builder.Property(webhookEvent => webhookEvent.ClaimedBy)
            .HasColumnName("claimed_by").HasColumnType("text");
        builder.Property(webhookEvent => webhookEvent.SucceededAt)
            .HasColumnName("succeeded_at").HasColumnType("timestamp with time zone");
        builder.Property(webhookEvent => webhookEvent.CreatedAt)
            .HasColumnName("created_at").HasColumnType("timestamp with time zone");
        builder.Property(webhookEvent => webhookEvent.UpdatedAt)
            .HasColumnName("updated_at").HasColumnType("timestamp with time zone");

        builder.HasIndex(webhookEvent => new { webhookEvent.DeliveryId, webhookEvent.Sequence })
            .IsUnique()
            .HasDatabaseName("webhook_events_delivery_id_sequence_key");
        builder.HasIndex(webhookEvent => new
            { webhookEvent.Status, webhookEvent.NextAttemptAt, webhookEvent.CreatedAt })
            .HasDatabaseName("webhook_events_due_idx");

        builder.HasOne<ApiClient>().WithMany().HasForeignKey(webhookEvent => webhookEvent.ApiClientId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<WebhookEndpoint>().WithMany().HasForeignKey(webhookEvent => webhookEvent.WebhookEndpointId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Message>().WithMany().HasForeignKey(webhookEvent => webhookEvent.MessageId)
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Delivery>().WithMany().HasForeignKey(webhookEvent => webhookEvent.DeliveryId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
