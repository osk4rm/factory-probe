using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SwarmApp.Api.Infrastructure;

public static class TelemetryExtensions
{
    public const string ServiceName = "swarm-app-api";

    public static IServiceCollection AddSwarmTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var endpoint = new Uri(
            configuration["OpenTelemetry:Endpoint"] ?? "http://localhost:4317");

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(ServiceName))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddOtlpExporter(options => options.Endpoint = endpoint))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(options => options.Endpoint = endpoint));

        return services;
    }
}
