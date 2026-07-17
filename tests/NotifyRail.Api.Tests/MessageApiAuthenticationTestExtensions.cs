using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotifyRail.Api.Features.ApiClients.CreateApiClient;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

internal static class MessageApiAuthenticationTestExtensions
{
    private const string OperatorCredential = "message-api-test-operator-credential";

    public static WebApplicationFactory<Program> WithMessageApiAuthentication(
        this WebApplicationFactory<Program> factory)
    {
        return factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Authentication:Operator:Credential", OperatorCredential));
    }

    public static async Task<HttpClient> CreateAuthenticatedMessageClientAsync(
        this WebApplicationFactory<Program> factory,
        string name)
    {
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
            await dbContext.Database.MigrateAsync();
        }

        using var operatorClient = factory.CreateClient();
        operatorClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Operator", OperatorCredential);
        using var response = await operatorClient.PostAsJsonAsync(
            "/management/api-clients",
            new { name = $"{name} {Guid.NewGuid()}" });
        response.EnsureSuccessStatusCode();
        var apiClient = await response.Content.ReadFromJsonAsync<CreateApiClientResponse>();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", apiClient!.ApiKey);
        return client;
    }
}
