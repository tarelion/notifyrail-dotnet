using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace NotifyRail.Api.Tests;

internal static class WithoutHostedServicesFactoryExtensions
{
    public static WebApplicationFactory<Program> WithoutHostedServices(
        this WebApplicationFactory<Program> factory)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
            });
        });
    }
}
