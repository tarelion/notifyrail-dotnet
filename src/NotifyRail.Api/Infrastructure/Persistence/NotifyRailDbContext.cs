using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Features.ApiClients.Persistence;
using NotifyRail.Api.Features.Deliveries.Persistence;
using NotifyRail.Api.Features.Messages.Persistence;
using NotifyRail.Api.Features.Otp.Persistence;
using NotifyRail.Api.Features.Webhooks.Persistence;

namespace NotifyRail.Api.Infrastructure.Persistence;

public sealed class NotifyRailDbContext(DbContextOptions<NotifyRailDbContext> options)
    : DbContext(options)
{
    public DbSet<ApiClient> ApiClients => Set<ApiClient>();

    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();

    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<OtpChallenge> OtpChallenges => Set<OtpChallenge>();

    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();

    public DbSet<WebhookSecret> WebhookSecrets => Set<WebhookSecret>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotifyRailDbContext).Assembly);
    }
}
