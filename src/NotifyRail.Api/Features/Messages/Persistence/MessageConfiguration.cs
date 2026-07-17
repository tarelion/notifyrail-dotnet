using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotifyRail.Api.Features.ApiClients.Persistence;

namespace NotifyRail.Api.Features.Messages.Persistence;

public sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.ToTable("messages", table =>
        {
            table.HasCheckConstraint(
                "messages_type_check",
                "type IN ('otp', 'transactional', 'campaign')");
            table.HasCheckConstraint(
                "messages_channel_check",
                "channel = 'sms'");
            table.HasCheckConstraint(
                "messages_sender_title_check",
                "btrim(sender_title) <> ''");
            table.HasCheckConstraint(
                "messages_body_check",
                "btrim(body) <> ''");
            table.HasCheckConstraint(
                "messages_idempotency_key_check",
                "btrim(idempotency_key) <> ''");
            table.HasCheckConstraint(
                "messages_encoding_check",
                "encoding IN ('latin', 'turkish', 'unicode')");
        });

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(message => message.ApiClientId)
            .HasColumnName("api_client_id")
            .IsRequired();

        builder.HasOne<ApiClient>()
            .WithMany()
            .HasForeignKey(message => message.ApiClientId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.Property(message => message.Type)
            .HasColumnName("type")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(message => message.Channel)
            .HasColumnName("channel")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(message => message.SenderTitle)
            .HasColumnName("sender_title")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(message => message.Body)
            .HasColumnName("body")
            .HasColumnType("text")
            .IsRequired();

        builder.Property(message => message.IdempotencyKey)
            .HasColumnName("idempotency_key")
            .HasColumnType("text")
            .IsRequired();

        builder.HasIndex(message => new { message.ApiClientId, message.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("messages_api_client_id_idempotency_key_key");

        builder.Property(message => message.ReportLabel)
            .HasColumnName("report_label")
            .HasColumnType("text");

        builder.Property(message => message.Encoding)
            .HasColumnName("encoding")
            .HasColumnType("text");

        builder.Property(message => message.ScheduledAt)
            .HasColumnName("scheduled_at")
            .HasColumnType("timestamp with time zone");

        builder.Property(message => message.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");

        builder.Property(message => message.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");
    }
}
