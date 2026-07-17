using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace NotifyRail.Api.Features.ApiClients.Persistence;

public sealed class ApiClientConfiguration : IEntityTypeConfiguration<ApiClient>
{
    public void Configure(EntityTypeBuilder<ApiClient> builder)
    {
        builder.ToTable("api_clients", table =>
        {
            table.HasCheckConstraint("api_clients_name_check", "btrim(name) <> ''");
            table.HasCheckConstraint(
                "api_clients_disabled_at_check",
                "(is_enabled AND disabled_at IS NULL) OR (NOT is_enabled AND disabled_at IS NOT NULL)");
        });

        builder.HasKey(apiClient => apiClient.Id);

        builder.Property(apiClient => apiClient.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");
        builder.Property(apiClient => apiClient.Name)
            .HasColumnName("name")
            .HasColumnType("text")
            .IsRequired();
        builder.Property(apiClient => apiClient.IsEnabled)
            .HasColumnName("is_enabled")
            .HasDefaultValue(true);
        builder.Property(apiClient => apiClient.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");
        builder.Property(apiClient => apiClient.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now()");
        builder.Property(apiClient => apiClient.DisabledAt)
            .HasColumnName("disabled_at")
            .HasColumnType("timestamp with time zone");
    }
}
