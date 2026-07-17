using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NotifyRail.Api.Features.ApiClients.Persistence;

public sealed class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> builder)
    {
        builder.ToTable("api_keys", table =>
        {
            table.HasCheckConstraint("api_keys_lookup_id_check", "btrim(lookup_id) <> ''");
            table.HasCheckConstraint("api_keys_verification_hash_check", "octet_length(verification_hash) = 32");
            table.HasCheckConstraint("api_keys_display_prefix_check", "btrim(display_prefix) <> ''");
        });

        builder.HasKey(apiKey => apiKey.Id);
        builder.Property(apiKey => apiKey.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(apiKey => apiKey.ApiClientId).HasColumnName("api_client_id");
        builder.Property(apiKey => apiKey.LookupId)
            .HasColumnName("lookup_id")
            .HasColumnType("text")
            .IsRequired();
        builder.HasIndex(apiKey => apiKey.LookupId)
            .IsUnique()
            .HasDatabaseName("api_keys_lookup_id_key");
        builder.Property(apiKey => apiKey.VerificationHash)
            .HasColumnName("verification_hash")
            .HasColumnType("bytea")
            .IsRequired();
        builder.Property(apiKey => apiKey.DisplayPrefix)
            .HasColumnName("display_prefix")
            .HasColumnType("text")
            .IsRequired();
        builder.Property(apiKey => apiKey.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");
        builder.Property(apiKey => apiKey.LastUsedAt)
            .HasColumnName("last_used_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(apiKey => apiKey.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamp with time zone");
        builder.Property(apiKey => apiKey.RevokedAt)
            .HasColumnName("revoked_at")
            .HasColumnType("timestamp with time zone");

        builder.HasOne<ApiClient>()
            .WithMany()
            .HasForeignKey(apiKey => apiKey.ApiClientId)
            .OnDelete(DeleteBehavior.NoAction);
    }
}
