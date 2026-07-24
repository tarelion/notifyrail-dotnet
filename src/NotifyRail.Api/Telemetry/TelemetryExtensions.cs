using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace NotifyRail.Api.Telemetry;

public static class TelemetryExtensions
{
    public static void AddNotifyRailTelemetry(this WebApplicationBuilder builder)
    {
        var endpointValue = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(endpointValue))
        {
            return;
        }
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException(
                "OTEL_EXPORTER_OTLP_ENDPOINT must be an absolute URI.");
        }

        builder.Services.Configure<OpenTelemetryLoggerOptions>(options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = false;
            options.ParseStateValues = true;
        });
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService("notifyrail-api"))
            .WithTracing(tracing => tracing
                .AddSource(NotifyRailTelemetry.ActivitySourceName)
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation())
            .UseOtlpExporter(OtlpExportProtocol.Grpc, endpoint);
    }
}
