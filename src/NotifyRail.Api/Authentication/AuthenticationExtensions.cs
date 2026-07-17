using Microsoft.AspNetCore.Authentication;

namespace NotifyRail.Api.Authentication;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddNotifyRailAuthentication(this IServiceCollection services)
    {
        services.AddOptions<OperatorAuthenticationOptions>()
            .BindConfiguration(OperatorAuthenticationOptions.SectionName)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.Credential),
                "Authentication:Operator:Credential is required.")
            .ValidateOnStart();

        services.AddAuthentication()
            .AddScheme<AuthenticationSchemeOptions, ApiClientAuthenticationHandler>(
                ApiClientAuthenticationHandler.SchemeName,
                _ => { })
            .AddScheme<AuthenticationSchemeOptions, OperatorAuthenticationHandler>(
                OperatorAuthenticationHandler.SchemeName,
                _ => { });

        services.AddAuthorizationBuilder()
            .AddPolicy(AuthenticationPolicies.ApiClient, policy =>
            {
                policy.AddAuthenticationSchemes(ApiClientAuthenticationHandler.SchemeName);
                policy.RequireAuthenticatedUser();
            })
            .AddPolicy(AuthenticationPolicies.Operator, policy =>
            {
                policy.AddAuthenticationSchemes(OperatorAuthenticationHandler.SchemeName);
                policy.RequireAuthenticatedUser();
            });

        return services;
    }
}
