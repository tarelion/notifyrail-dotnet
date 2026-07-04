using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Features.Deliveries.Persistence;
using NotifyRail.Api.Features.Messages.Persistence;

namespace NotifyRail.Api.Infrastructure.Persistence;

public sealed class NotifyRailDbContext(DbContextOptions<NotifyRailDbContext> options)
    : DbContext(options)
{
    public DbSet<DeliveryAttempt> DeliveryAttempts => Set<DeliveryAttempt>();

    public DbSet<Delivery> Deliveries => Set<Delivery>();

    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotifyRailDbContext).Assembly);
    }
}
