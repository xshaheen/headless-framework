// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using Headless.Checks;
using Headless.Serializer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Headless.Api;

[PublicAPI]
public static class ServiceDefaultsSetup
{
    private const string _HeadlessWildcardSourceName = "Headless.*";

    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddHeadlessServiceDefaults(
            Action<HeadlessApiInfrastructureOptions>? configure = null
        )
        {
            Argument.IsNotNull(builder);

            var options = new HeadlessApiInfrastructureOptions();
            configure?.Invoke(options);

            return builder._AddServiceDefaults(options);
        }

        internal WebApplicationBuilder AddHeadlessServiceDefaults(HeadlessApiInfrastructureOptions options)
        {
            Argument.IsNotNull(builder);
            Argument.IsNotNull(options);

            return builder._AddServiceDefaults(options);
        }

        private WebApplicationBuilder _AddServiceDefaults(HeadlessApiInfrastructureOptions options)
        {
            if (options.ValidateDependencyContainerOnStartup)
            {
                builder.Host.UseDefaultServiceProvider(serviceProviderOptions =>
                {
                    serviceProviderOptions.ValidateOnBuild = true;
                    serviceProviderOptions.ValidateScopes = true;
                });
            }

            builder.Services.TryAddSingleton(options);
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IStartupFilter, HeadlessApiInfrastructureValidationStartupFilter>()
            );
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedLifecycleService, HeadlessApiInfrastructureValidationStartupFilter>()
            );

            builder._ConfigureOpenTelemetry(options);

            if (options.OpenApi.Enabled)
            {
                builder.Services.AddOpenApi(options.OpenApi.ConfigureOpenApi ?? (_ => { }));
            }

            if (options.HttpClient.UseServiceDiscovery)
            {
                builder.Services.AddServiceDiscovery();
            }

            builder.Services.ConfigureHttpClientDefaults(http =>
            {
                if (options.HttpClient.UseStandardResilienceHandler)
                {
                    http.AddStandardResilienceHandler();
                }

                if (options.HttpClient.UseServiceDiscovery)
                {
                    http.AddServiceDiscovery();
                }

                if (options.HttpClient.AddApplicationUserAgent)
                {
                    http.ConfigureHttpClient(
                        (serviceProvider, client) =>
                        {
                            var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
                            var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
                            client.DefaultRequestHeaders.UserAgent.Add(
                                new ProductInfoHeaderValue(environment.ApplicationName, version)
                            );
                        }
                    );
                }
            });

            builder.Services.Configure<MvcJsonOptions>(jsonOptions =>
                _ConfigureJsonOptions(jsonOptions.JsonSerializerOptions, options)
            );
            builder.Services.Configure<HttpJsonOptions>(jsonOptions =>
                _ConfigureJsonOptions(jsonOptions.SerializerOptions, options)
            );
            builder.Services.AddValidation();

            return builder;
        }

        private WebApplicationBuilder _ConfigureOpenTelemetry(HeadlessApiInfrastructureOptions options)
        {
            if (!options.OpenTelemetry.Enabled)
            {
                return builder;
            }

            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
                options.OpenTelemetry.ConfigureLogging?.Invoke(logging);
            });

            var openTelemetry = builder
                .Services.AddOpenTelemetry()
                .ConfigureResource(resource =>
                {
                    var name = Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME");

                    if (string.IsNullOrWhiteSpace(name))
                    {
                        name = builder.Environment.ApplicationName;
                    }

                    var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
                    resource.AddService(name, serviceVersion: version);
                })
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation()
                        .AddMeter(_HeadlessWildcardSourceName);

                    options.OpenTelemetry.ConfigureMetrics?.Invoke(metrics);
                })
                .WithTracing(tracing =>
                {
                    tracing
                        .AddSource(builder.Environment.ApplicationName)
                        .AddAspNetCoreInstrumentation(instrumentation =>
                        {
                            instrumentation.EnableAspNetCoreSignalRSupport = true;
                            instrumentation.Filter = context =>
                                context.Request.Path != "/health" && context.Request.Path != "/alive";
                        })
                        .AddHttpClientInstrumentation()
                        .AddSource(_HeadlessWildcardSourceName);

                    options.OpenTelemetry.ConfigureTracing?.Invoke(tracing);
                });

            var useOtlpExporter =
                options.OpenTelemetry.UseOtlpExporterWhenEndpointConfigured
                && !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

            if (useOtlpExporter)
            {
                openTelemetry.UseOtlpExporter();
            }

            return builder;
        }
    }

    private static void _ConfigureJsonOptions(
        JsonSerializerOptions jsonOptions,
        HeadlessApiInfrastructureOptions options
    )
    {
        JsonConstants.ConfigureWebJsonOptions(jsonOptions);
        options.ConfigureJsonOptions?.Invoke(jsonOptions);
    }
}
