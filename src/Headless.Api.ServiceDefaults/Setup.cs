// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using FileSignatures;
using FluentValidation;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Identity.Normalizer;
using Headless.Api.Identity.Schemes;
using Headless.Api.Security.Claims;
using Headless.Api.Security.Jwt;
using Headless.Checks;
using Headless.Constants;
using Headless.Serializer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Headless.Api;

[PublicAPI]
public static class ApiSetup
{
    private const string _StringEncryptionSectionName = "Headless:StringEncryption";
    private const string _StringHashSectionName = "Headless:StringHash";
    private const string _HeadlessWildcardSourceName = "Headless.*";

    public static readonly FileFormatInspector FileFormatInspector = new(FileFormatLocator.GetFormats());

    public static void ConfigureGlobalSettings()
    {
        AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(1));
        ValidatorOptions.Global.LanguageManager.Enabled = true;
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;
        JsonWebTokenHandler.DefaultMapInboundClaims = false;
        JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();
    }

    extension(WebApplicationBuilder builder)
    {
        public WebApplicationBuilder AddHeadless(Action<HeadlessServiceDefaultsOptions>? configureServices = null)
        {
            Argument.IsNotNull(builder);

            builder._AddDefaultStringEncryptionService();
            builder._AddDefaultStringHashService();

            return builder._AddApiCore(configureServices);
        }

        public WebApplicationBuilder AddHeadless(
            IConfiguration stringEncryptionConfig,
            IConfiguration stringHashConfig,
            Action<HeadlessServiceDefaultsOptions>? configureServices = null
        )
        {
            Argument.IsNotNull(builder);
            Argument.IsNotNull(stringEncryptionConfig);
            Argument.IsNotNull(stringHashConfig);

            builder.Services.AddStringEncryptionService(stringEncryptionConfig);
            builder.Services.AddStringHashService(stringHashConfig);

            return builder._AddApiCore(configureServices);
        }

        public WebApplicationBuilder AddHeadless(
            Action<StringEncryptionOptions> configureEncryption,
            Action<StringHashOptions>? configureHash = null,
            Action<HeadlessServiceDefaultsOptions>? configureServices = null
        )
        {
            Argument.IsNotNull(builder);
            Argument.IsNotNull(configureEncryption);

            builder.Services.AddStringEncryptionService(configureEncryption);

            if (configureHash is null)
            {
                builder._AddDefaultStringHashService();
            }
            else
            {
                builder.Services.AddStringHashService(configureHash);
            }

            return builder._AddApiCore(configureServices);
        }

        public WebApplicationBuilder AddHeadless(
            Action<StringEncryptionOptions, IServiceProvider> configureEncryption,
            Action<StringHashOptions, IServiceProvider>? configureHash = null,
            Action<HeadlessServiceDefaultsOptions>? configureServices = null
        )
        {
            Argument.IsNotNull(builder);
            Argument.IsNotNull(configureEncryption);

            builder.Services.AddStringEncryptionService(configureEncryption);

            if (configureHash is null)
            {
                builder._AddDefaultStringHashService();
            }
            else
            {
                builder.Services.AddStringHashService(configureHash);
            }

            return builder._AddApiCore(configureServices);
        }

        private void _AddDefaultStringEncryptionService()
        {
            builder.Services.AddStringEncryptionService(
                builder.Configuration.GetRequiredSection(_StringEncryptionSectionName)
            );
        }

        private void _AddDefaultStringHashService()
        {
            builder.Services.AddStringHashService(builder.Configuration.GetRequiredSection(_StringHashSectionName));
        }

        private WebApplicationBuilder _AddApiCore(Action<HeadlessServiceDefaultsOptions>? configureServices)
        {
            var options = new HeadlessServiceDefaultsOptions();
            configureServices?.Invoke(options);

            if (options.Validation.ValidateServiceProviderOnStartup)
            {
                builder.Host.UseDefaultServiceProvider(serviceProviderOptions =>
                {
                    serviceProviderOptions.ValidateOnBuild = true;
                    serviceProviderOptions.ValidateScopes = true;
                });
            }

            builder.Services.TryAddSingleton(options);
            builder.Services.TryAddSingleton<HeadlessServiceDefaultsValidationStartupFilter>();
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IStartupFilter, HeadlessServiceDefaultsValidationStartupFilter>(sp =>
                    sp.GetRequiredService<HeadlessServiceDefaultsValidationStartupFilter>()
                )
            );
            builder.Services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IHostedLifecycleService, HeadlessServiceDefaultsValidationStartupFilter>(
                    sp => sp.GetRequiredService<HeadlessServiceDefaultsValidationStartupFilter>()
                )
            );

            // Core API primitives
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddResilienceEnricher();
            builder.Services.AddHeadlessJsonService();
            builder.Services.AddHeadlessTimeService();
            builder.Services.AddHeadlessApiResponseCompression();
            builder.Services.AddHeadlessProblemDetails();
            builder.Services.AddStatusCodesRewriterMiddleware();
            builder.Services.ConfigureHeadlessDefaultApi();
            builder.Services.AddHeadlessAntiforgery();
            builder.Services.AddValidation();
            builder.Services.Configure<MvcJsonOptions>(jsonOptions =>
                JsonConstants.ConfigureWebJsonOptions(jsonOptions.JsonSerializerOptions)
            );
            builder.Services.Configure<HttpJsonOptions>(jsonOptions =>
                JsonConstants.ConfigureWebJsonOptions(jsonOptions.SerializerOptions)
            );

            builder.Services.TryAddSingleton<IGuidGenerator, SequentialAtEndGuidGenerator>();
            builder.Services.TryAddSingleton<ILongIdGenerator>(new SnowflakeIdLongIdGenerator(1));
            builder.Services.TryAddSingleton<IEnumLocaleAccessor, DefaultEnumLocaleAccessor>();
            builder.Services.TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>();
            builder.Services.TryAddSingleton<IApplicationInformationAccessor, ApplicationInformationAccessor>();
            builder.Services.TryAddSingleton<ICancellationTokenProvider, HttpContextCancellationTokenProvider>();

            builder.Services.TryAddSingleton<IPasswordGenerator, PasswordGenerator>();
            builder.Services.TryAddSingleton<IFileFormatInspector>(FileFormatInspector);
            builder.Services.TryAddSingleton<IMimeTypeProvider, MimeTypeProvider>();
            builder.Services.TryAddSingleton<IContentTypeProvider, ExtendedFileExtensionContentTypeProvider>();

            builder.Services.TryAddSingleton<IClaimsPrincipalFactory, ClaimsPrincipalFactory>();
            builder.Services.TryAddSingleton<IJwtTokenFactory, JwtTokenFactory>();

            builder.Services.TryAddSingleton<ICurrentLocale, CurrentCultureCurrentLocale>();
            builder.Services.TryAddSingleton<ICurrentPrincipalAccessor, HttpContextCurrentPrincipalAccessor>();
            builder.Services.TryAddSingleton<ICurrentUser, HttpCurrentUser>();
            builder.Services.TryAddSingleton<ICurrentTimeZone, LocalCurrentTimeZone>();
            builder.Services.TryAddSingleton<ICurrentTenantAccessor>(AsyncLocalCurrentTenantAccessor.Instance);
            // Removes NullCurrentTenant fallback; preserves consumer-supplied ICurrentTenant.
            builder.Services.AddOrReplaceFallbackSingleton<ICurrentTenant, NullCurrentTenant, CurrentTenant>();
            builder.Services.TryAddSingleton<IWebClientInfoProvider, HttpWebClientInfoProvider>();

            builder.Services.TryAddScoped<IRequestContext, HttpRequestContext>();
            builder.Services.TryAddScoped<IAbsoluteUrlFactory, HttpAbsoluteUrlFactory>();
            builder.Services.TryAddScoped<IRequestedApiVersion, HttpContextRequestedApiVersion>();

            builder.Services.AddOrReplaceSingleton<ILookupNormalizer, HeadlessLookupNormalizer>();
            builder.Services.AddOrReplaceSingleton<
                IAuthenticationSchemeProvider,
                DynamicAuthenticationSchemeProvider
            >();

            // Aspire-style service defaults (OpenTelemetry, OpenAPI, service discovery, HttpClient resilience)
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

            return builder;
        }

        private WebApplicationBuilder _ConfigureOpenTelemetry(HeadlessServiceDefaultsOptions options)
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

    /// <summary>Applies the default Headless API middleware order. Idempotent.</summary>
    public static WebApplication UseHeadless(this WebApplication app, Action<HeadlessApiDefaultsOptions>? configure = null)
    {
        Argument.IsNotNull(app);

        var applicationBuilder = (IApplicationBuilder)app;

        if (applicationBuilder.Properties.ContainsKey(HeadlessApiDefaultsOptions.AppliedKey))
        {
            return app;
        }

        var options = new HeadlessApiDefaultsOptions();
        configure?.Invoke(options);

        applicationBuilder.Properties[HeadlessApiDefaultsOptions.AppliedKey] = true;

        if (options.UseForwardedHeaders)
        {
            var forwardedHeadersOptions = new ForwardedHeadersOptions { ForwardedHeaders = options.ForwardedHeaders };

            if (options.TrustForwardedHeadersFromAnyProxy)
            {
                forwardedHeadersOptions.KnownIPNetworks.Clear();
                forwardedHeadersOptions.KnownProxies.Clear();
            }

            options.ConfigureForwardedHeaders?.Invoke(forwardedHeadersOptions);
            app.UseForwardedHeaders(forwardedHeadersOptions);
        }

        if (options.UseResponseCompression)
        {
            app.UseResponseCompression();
        }

        if (options.UseStatusCodePages)
        {
            app.UseStatusCodePages();
            app.UseStatusCodesRewriter();
        }

        if (options.UseExceptionHandler)
        {
            if (string.IsNullOrWhiteSpace(options.ExceptionHandlerPath))
            {
                app.UseExceptionHandler();
            }
            else
            {
                app.UseExceptionHandler(options.ExceptionHandlerPath, options.CreateScopeForErrors);
            }
        }

        if (options.UseHttpsRedirection)
        {
            app.UseHttpsRedirection();
        }

        if (options.UseHsts && !app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        if (options.UseAntiforgery)
        {
            app.UseAntiforgery();
        }

        if (options.SetNoCacheWhenMissingCacheHeaders)
        {
            app.UseNoCacheWhenMissingCacheHeaders();
        }

        if (app.Services.GetService<HeadlessServiceDefaultsOptions>() is { } serviceOptions)
        {
            serviceOptions.UseHeadlessCalled = true;
        }

        return app;
    }

    /// <summary>Maps the default Headless API operational and convention endpoints. Idempotent.</summary>
    public static WebApplication MapHeadlessEndpoints(
        this WebApplication app,
        Action<HeadlessApiDefaultEndpointOptions>? configure = null
    )
    {
        Argument.IsNotNull(app);

        var applicationBuilder = (IApplicationBuilder)app;

        if (applicationBuilder.Properties.ContainsKey(HeadlessApiDefaultEndpointOptions.AppliedKey))
        {
            return app;
        }

        var options = new HeadlessApiDefaultEndpointOptions();
        configure?.Invoke(options);

        applicationBuilder.Properties[HeadlessApiDefaultEndpointOptions.AppliedKey] = true;

        if (options.MapHealthEndpoint)
        {
            var healthChecks = app.MapHealthChecks(
                options.HealthPath,
                new HealthCheckOptions { ResponseWriter = options.HealthResponseWriter }
            );

            _ConfigureOperationalEndpoint(healthChecks, options.HealthEndpointName, options);
        }

        if (options.MapAliveEndpoint)
        {
            var aliveCheck = app.MapHealthChecks(
                options.AlivePath,
                new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(options.AliveTag) }
            );

            _ConfigureOperationalEndpoint(aliveCheck, options.AliveEndpointName, options);
        }

        var serviceOptions = app.Services.GetService<HeadlessServiceDefaultsOptions>();

        if (serviceOptions?.StaticAssets.Enabled is true && _StaticWebAssetsManifestExists(app))
        {
            app.MapStaticAssets();
        }

        if (serviceOptions?.OpenApi.Enabled is true)
        {
            var openApi = app.MapOpenApi(serviceOptions.OpenApi.RoutePattern);

            if (serviceOptions.OpenApi.CacheDocument)
            {
                openApi.CacheOutput();
            }
        }

        if (serviceOptions is not null)
        {
            serviceOptions.MapHeadlessEndpointsCalled = true;
        }

        return app;
    }

    private static void _ConfigureOperationalEndpoint(
        IEndpointConventionBuilder endpoint,
        string name,
        HeadlessApiDefaultEndpointOptions options
    )
    {
        if (!string.IsNullOrWhiteSpace(name))
        {
            endpoint.WithName(name);
        }

        if (options.ExcludeFromDescription)
        {
            endpoint.ExcludeFromDescription();
        }

        if (options.AllowAnonymous)
        {
            endpoint.AllowAnonymous();
        }
    }

    private static bool _StaticWebAssetsManifestExists(WebApplication app)
    {
        var environment = app.Services.GetRequiredService<IWebHostEnvironment>();
        var staticAssetsManifestPath = $"{environment.ApplicationName}.staticwebassets.endpoints.json";

        if (!Path.IsPathRooted(staticAssetsManifestPath))
        {
            staticAssetsManifestPath = Path.Combine(AppContext.BaseDirectory, staticAssetsManifestPath);
        }

        return File.Exists(staticAssetsManifestPath);
    }

    internal static async Task WriteHealthReportAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = ContentTypes.Applications.Json;

        await using var writer = new Utf8JsonWriter(context.Response.Body);
        writer.WriteStartObject();
        writer.WriteString("status", report.Status.ToString());
        writer.WriteStartObject("results");

        foreach (var (name, entry) in report.Entries)
        {
            writer.WriteStartObject(name);
            writer.WriteString("status", entry.Status.ToString());

            if (entry.Description is not null)
            {
                writer.WriteString("description", entry.Description);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync(context.RequestAborted).ConfigureAwait(false);
    }
}
