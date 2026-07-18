using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NotifyRail.Api.Features.ApiClients.Persistence;

namespace NotifyRail.Api.Features.Webhooks.Persistence;

public sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("webhook_endpoints", table =>
        {
            table.HasCheckConstraint("webhook_endpoints_url_check", "btrim(url) <> ''");
            table.HasCheckConstraint(
                "webhook_endpoints_disabled_at_check",
                "(is_enabled AND disabled_at IS NULL) OR (NOT is_enabled AND disabled_at IS NOT NULL)");
        });

        builder.HasKey(endpoint => endpoint.Id);
        builder.Property(endpoint => endpoint.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(endpoint => endpoint.ApiClientId).HasColumnName("api_client_id");
        builder.Property(endpoint => endpoint.Url)
            .HasColumnName("url")
            .HasColumnType("text")
            .IsRequired();
        builder.Property(endpoint => endpoint.IsEnabled)
            .HasColumnName("is_enabled")
            .HasDefaultValue(true);
        builder.Property(endpoint => endpoint.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");
        builder.Property(endpoint => endpoint.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");
        builder.Property(endpoint => endpoint.DisabledAt)
            .HasColumnName("disabled_at")
            .HasColumnType("timestamp with time zone");

        builder.HasIndex(endpoint => endpoint.ApiClientId)
            .IsUnique()
            .HasFilter("is_enabled")
            .HasDatabaseName("webhook_endpoints_active_api_client_id_key");
        builder.HasIndex(endpoint => new { endpoint.ApiClientId, endpoint.CreatedAt })
            .HasDatabaseName("webhook_endpoints_api_client_created_at_idx");

        builder.HasOne<ApiClient>()
            .WithMany()
            .HasForeignKey(endpoint => endpoint.ApiClientId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
