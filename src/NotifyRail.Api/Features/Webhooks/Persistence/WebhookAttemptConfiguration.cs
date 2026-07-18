using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NotifyRail.Api.Features.Webhooks.Persistence;

public sealed class WebhookAttemptConfiguration : IEntityTypeConfiguration<WebhookAttempt>
{
    public void Configure(EntityTypeBuilder<WebhookAttempt> builder)
    {
        builder.ToTable("webhook_attempts", table =>
        {
            table.HasCheckConstraint("webhook_attempts_attempt_number_check", "attempt_number > 0");
            table.HasCheckConstraint(
                "webhook_attempts_outcome_check",
                "outcome IN ('succeeded', 'retryable_failure', 'permanent_failure')");
            table.HasCheckConstraint(
                "webhook_attempts_http_status_code_check",
                "http_status_code IS NULL OR http_status_code BETWEEN 100 AND 599");
            table.HasCheckConstraint(
                "webhook_attempts_error_code_check",
                "error_code IS NULL OR (btrim(error_code) <> '' AND char_length(error_code) <= 100)");
            table.HasCheckConstraint(
                "webhook_attempts_error_message_check",
                "error_message IS NULL OR (btrim(error_message) <> '' AND char_length(error_message) <= 500)");
            table.HasCheckConstraint("webhook_attempts_latency_check", "latency_milliseconds >= 0");
            table.HasCheckConstraint("webhook_attempts_time_check", "completed_at >= attempted_at");
        });

        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(attempt => attempt.WebhookEventId).HasColumnName("webhook_event_id");
        builder.Property(attempt => attempt.AttemptNumber).HasColumnName("attempt_number");
        builder.Property(attempt => attempt.Outcome).HasColumnName("outcome").HasColumnType("text");
        builder.Property(attempt => attempt.HttpStatusCode).HasColumnName("http_status_code");
        builder.Property(attempt => attempt.ErrorCode).HasColumnName("error_code").HasColumnType("text");
        builder.Property(attempt => attempt.ErrorMessage).HasColumnName("error_message").HasColumnType("text");
        builder.Property(attempt => attempt.AttemptedAt)
            .HasColumnName("attempted_at").HasColumnType("timestamp with time zone");
        builder.Property(attempt => attempt.CompletedAt)
            .HasColumnName("completed_at").HasColumnType("timestamp with time zone");
        builder.Property(attempt => attempt.LatencyMilliseconds).HasColumnName("latency_milliseconds");

        builder.HasIndex(attempt => new { attempt.WebhookEventId, attempt.AttemptNumber })
            .IsUnique()
            .HasDatabaseName("webhook_attempts_event_id_attempt_number_key");
        builder.HasOne<WebhookEvent>().WithMany().HasForeignKey(attempt => attempt.WebhookEventId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
