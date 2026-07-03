namespace NotifyRail.Api.Features.Health;

public static class HealthExtensions
{
    public static IServiceCollection AddNotifyRailHealth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres");

        services.AddSingleton<IReadinessCheck>(
            string.IsNullOrWhiteSpace(connectionString)
                ? new MissingConfigurationReadinessCheck()
                : new PostgresReadinessCheck(connectionString));

        return services;
    }
}
