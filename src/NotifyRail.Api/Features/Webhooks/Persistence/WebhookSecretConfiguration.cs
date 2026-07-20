using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotifyRail.Api.Features.ApiClients.Persistence;

namespace NotifyRail.Api.Features.Webhooks.Persistence;

public sealed class WebhookSecretConfiguration : IEntityTypeConfiguration<WebhookSecret>
{
    public void Configure(EntityTypeBuilder<WebhookSecret> builder)
    {
        builder.ToTable("webhook_secrets", table =>
        {
            table.HasCheckConstraint(
                "webhook_secrets_protected_value_check",
                "octet_length(protected_value) > 0");
            table.HasCheckConstraint(
                "webhook_secrets_retired_at_check",
                "retired_at IS NULL OR retired_at >= created_at");
        });

        builder.HasKey(secret => secret.Id);
        builder.Property(secret => secret.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(secret => secret.ApiClientId).HasColumnName("api_client_id");
        builder.Property(secret => secret.ProtectedValue)
            .HasColumnName("protected_value")
            .HasColumnType("bytea")
            .IsRequired();
        builder.Property(secret => secret.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");
        builder.Property(secret => secret.RetiredAt)
            .HasColumnName("retired_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(secret => secret.ApiClientId)
            .IsUnique()
            .HasFilter("retired_at IS NULL")
            .HasDatabaseName("webhook_secrets_active_api_client_id_key");

        builder.HasIndex(secret => new { secret.ApiClientId, secret.CreatedAt })
            .HasDatabaseName("webhook_secrets_api_client_created_at_idx");

        builder.HasOne<ApiClient>()
            .WithMany()
            .HasForeignKey(secret => secret.ApiClientId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
