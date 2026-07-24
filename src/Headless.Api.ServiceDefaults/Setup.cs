// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Buffers;
using System.ComponentModel;
using System.Net.Http.Headers;
using System.Reflection;
using FileSignatures;
using FluentValidation;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Identity.Normalizer;
using Headless.Api.Identity.Schemes;
using Headless.Api.Security.Claims;
using Headless.Api.Security.Jwt;
using Headless.Api.UserAgent;
using Headless.Caching;
using Headless.Checks;
using Headless.Constants;
using Headless.Security;
using Headless.Serializer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using HttpJsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;
using MvcJsonOptions = Microsoft.AspNetCore.Mvc.JsonOptions;

namespace Headless.Api.ServiceDefaults;

[PublicAPI]
public static class SetupApi
{
    private const string _StringEncryptionSectionName = "Headless:StringEncryption";
    private const string _StringHashSectionName = "Headless:StringHash";
    private const string _HeadlessWildcardSourceName = "Headless.*";
    private static int _globalSettingsConfigured;

    /// <summary>
    /// Applies one-time process-wide defaults: regex timeout, FluentValidation cascade mode, and JWT claim mapping.
    /// Idempotent — subsequent calls are no-ops.
    /// </summary>
    public static void ConfigureGlobalSettings()
    {
        if (Interlocked.Exchange(ref _globalSettingsConfigured, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(1));
        ValidatorOptions.Global.LanguageManager.Enabled = true;
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;
        JsonWebTokenHandler.DefaultMapInboundClaims = false;
        JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();
    }

    /// <summary>
    /// Resets the global-settings guard so the next <see cref="ConfigureGlobalSettings"/> call re-applies all defaults.
    /// Intended for unit tests only — never call this in production code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    internal static void ResetForTesting()
    {
        Volatile.Write(ref _globalSettingsConfigured, 0);
    }

    extension(WebApplicationBuilder builder)
    {
        /// <summary>
        /// Registers all Headless service defaults (OpenTelemetry, OpenAPI, HttpClient, service discovery,
        /// problem details, multi-tenancy stubs, etc.) and reads encryption/hash secrets from the default
        /// <c>Headless:StringEncryption</c> and <c>Headless:StringHash</c> configuration sections.
        /// </summary>
        /// <param name="configureServices">Optional callback to tune <see cref="HeadlessServiceDefaultsOptions"/> before registration.</param>
        /// <returns><paramref name="builder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        public WebApplicationBuilder AddHeadless(Action<HeadlessServiceDefaultsOptions>? configureServices = null)
        {
            Argument.IsNotNull(builder);

            builder._AddDefaultStringEncryptionService();
            builder._AddDefaultStringHashService();

            return builder._AddApiCore(configureServices);
        }

        /// <summary>
        /// Registers all Headless service defaults, binding encryption and hash options from the
        /// supplied <see cref="IConfiguration"/> sections instead of the default
        /// <c>Headless:StringEncryption</c> / <c>Headless:StringHash</c> paths.
        /// </summary>
        /// <param name="stringEncryptionConfig">Configuration section for string-encryption options.</param>
        /// <param name="stringHashConfig">Configuration section for string-hash options.</param>
        /// <param name="configureServices">Optional callback to tune <see cref="HeadlessServiceDefaultsOptions"/>.</param>
        /// <returns><paramref name="builder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/>, <paramref name="stringEncryptionConfig"/>, or <paramref name="stringHashConfig"/> is <see langword="null"/>.</exception>
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

        /// <summary>
        /// Registers all Headless service defaults, configuring encryption options via a delegate.
        /// Hash options default to the <c>Headless:StringHash</c> configuration section when
        /// <paramref name="configureHash"/> is <see langword="null"/>.
        /// </summary>
        /// <param name="configureEncryption">Required callback to configure string-encryption options.</param>
        /// <param name="configureHash">Optional callback to configure string-hash options.</param>
        /// <param name="configureServices">Optional callback to tune <see cref="HeadlessServiceDefaultsOptions"/>.</param>
        /// <returns><paramref name="builder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configureEncryption"/> is <see langword="null"/>.</exception>
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

        /// <summary>
        /// Registers all Headless service defaults, configuring encryption options via a service-provider
        /// delegate. Hash options default to the <c>Headless:StringHash</c> configuration section when
        /// <paramref name="configureHash"/> is <see langword="null"/>.
        /// </summary>
        /// <param name="configureEncryption">Required callback (with <see cref="IServiceProvider"/>) to configure encryption options.</param>
        /// <param name="configureHash">Optional callback (with <see cref="IServiceProvider"/>) to configure hash options.</param>
        /// <param name="configureServices">Optional callback to tune <see cref="HeadlessServiceDefaultsOptions"/>.</param>
        /// <returns><paramref name="builder"/> for chaining.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> or <paramref name="configureEncryption"/> is <see langword="null"/>.</exception>
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
            ConfigureGlobalSettings();

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

            var startupState = new HeadlessStartupState();
            builder.Services.TryAddSingleton(options);
            builder.Services.TryAddSingleton(startupState);
            builder.Services.TryAddSingleton<IStatusCodesRewriterCalledNotifier>(
                _ => new StatusCodesRewriterCalledNotifier(startupState)
            );
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

            if (options.Antiforgery.Enabled)
            {
                builder.Services.AddHeadlessAntiforgery();
            }

            builder.Services.AddValidation();
            builder.Services.Configure<MvcJsonOptions>(jsonOptions =>
                JsonConstants.ConfigureWebJsonOptions(jsonOptions.JsonSerializerOptions)
            );
            builder.Services.Configure<HttpJsonOptions>(jsonOptions =>
                JsonConstants.ConfigureWebJsonOptions(jsonOptions.SerializerOptions)
            );

            builder.Services.AddHeadlessGuidGenerator();
            builder.Services.TryAddSingleton<IEnumLocaleAccessor, DefaultEnumLocaleAccessor>();
            builder.Services.TryAddSingleton<IBuildInformationAccessor, BuildInformationAccessor>();
            builder.Services.TryAddSingleton<IApplicationInformationAccessor, ApplicationInformationAccessor>();
            builder.Services.TryAddSingleton<ICancellationTokenProvider, HttpContextCancellationTokenProvider>();

            builder.Services.TryAddSingleton<IPasswordGenerator, PasswordGenerator>();
            builder.Services.TryAddSingleton<IFileFormatInspector>(_ => new FileFormatInspector(
                FileFormatLocator.GetFormats()
            ));
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
            builder.Services.AddOptions<UserAgentParserOptions, UserAgentParserOptionsValidator>();
            // ICache is resolved with GetService (optional): User-Agent parses are memoized only when the host
            // registers a default Headless.Caching provider; otherwise the parser runs on every call.
            builder.Services.TryAddSingleton<IUserAgentParser>(sp => new UserAgentParser(
                sp.GetRequiredService<IOptions<UserAgentParserOptions>>(),
                sp.GetService<ICache>()
            ));
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
                            var appAccessor = serviceProvider.GetRequiredService<IApplicationInformationAccessor>();
                            var buildAccessor = serviceProvider.GetRequiredService<IBuildInformationAccessor>();
                            client.DefaultRequestHeaders.UserAgent.Add(
                                new ProductInfoHeaderValue(appAccessor.ApplicationName, buildAccessor.GetVersion())
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
                            var otel = options.OpenTelemetry;
                            instrumentation.EnableAspNetCoreSignalRSupport = true;
                            instrumentation.RecordException = otel.RecordException;

                            // Capture otel by reference so MapHeadlessEndpoints() can replace
                            // SkipOperationalEndpointFunc with a delegate built from the actual
                            // configured paths before any requests start flowing.
                            instrumentation.Filter =
                                otel.Filter ?? (context => !otel.SkipOperationalEndpointFunc(context));

                            // User hook runs LAST so it can override Filter, add enrichers, etc.
                            otel.ConfigureAspNetCoreInstrumentation?.Invoke(instrumentation);
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

    /// <summary>
    /// Applies the default Headless API middleware pipeline in the correct order. Idempotent — subsequent
    /// calls on the same <paramref name="app"/> instance are no-ops.
    /// </summary>
    /// <param name="app">The <see cref="WebApplication"/> to configure.</param>
    /// <param name="configure">
    /// Optional callback to tune <see cref="HeadlessApiDefaultsOptions"/> — forwarded headers,
    /// response compression, status-code pages, exception handling, HTTPS redirection, HSTS,
    /// and no-cache injection.
    /// </param>
    /// <returns><paramref name="app"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Must be called before the application starts for startup validation to pass when
    /// <see cref="HeadlessServiceDefaultsValidationOptions.RequireUseHeadless"/> is <see langword="true"/> (the default).
    /// </remarks>
    public static WebApplication UseHeadless(
        this WebApplication app,
        Action<HeadlessApiDefaultsOptions>? configure = null
    )
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

        if (options.SetNoCacheWhenMissingCacheHeaders)
        {
            app.UseNoCacheWhenMissingCacheHeaders();
        }

        if (app.Services.GetService<HeadlessStartupState>() is { } startupState)
        {
            startupState.UseHeadlessCalled = true;
        }

        return app;
    }

    /// <summary>
    /// Maps the default Headless API operational and convention endpoints (health, alive, OpenAPI, static assets).
    /// Idempotent — subsequent calls on the same <paramref name="app"/> instance are no-ops.
    /// </summary>
    /// <param name="app">The <see cref="WebApplication"/> to configure.</param>
    /// <param name="configure">
    /// Optional callback to tune <see cref="HeadlessApiDefaultEndpointOptions"/> — endpoint paths,
    /// route names, anonymous access, OpenAPI inclusion, and liveness tag.
    /// </param>
    /// <returns><paramref name="app"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="app"/> is <see langword="null"/>.</exception>
    /// <remarks>
    /// Must be called before the application starts for startup validation to pass when
    /// <see cref="HeadlessServiceDefaultsValidationOptions.RequireMapHeadlessEndpoints"/> is <see langword="true"/> (the default).
    /// This call also updates the OpenTelemetry tracing filter with the actual configured health and alive paths,
    /// so it should run after all <c>AddHeadless</c> configuration is complete.
    /// </remarks>
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

        var serviceOptions = app.Services.GetService<HeadlessServiceDefaultsOptions>();

        // Publish the configured operational paths to the default OTel tracing filter, so consumer
        // overrides (e.g. options.HealthPath = "/healthz") are excluded from traces. The delegate is
        // replaced atomically here; no mutable fields are read after the tracing provider captures
        // its snapshot.
        serviceOptions?.OpenTelemetry.SkipOperationalEndpointFunc =
            HeadlessServiceDefaultsOpenTelemetryOptions.BuildSkipFunc(
                healthPath: options.HealthPath,
                alivePath: options.AlivePath,
                healthMapped: options.MapHealthEndpoint,
                aliveMapped: options.MapAliveEndpoint
            );

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

        if (app.Services.GetService<HeadlessStartupState>() is { } mapStartupState)
        {
            mapStartupState.MapHeadlessEndpointsCalled = true;
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
        // Serialize into a buffer first so that a serialization fault cannot emit a partial body
        // to load-balancer pollers. Headers are set only after the buffer is ready.
        var buffer = new ArrayBufferWriter<byte>();

        try
        {
            await using var writer = new Utf8JsonWriter(buffer);
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
            // Flush to the in-memory buffer (no I/O cancellation needed here).
            await writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Serialization failed before any bytes reached the client — set 500 and bail.
            // Abort the connection so the client sees a clean error rather than a partial body.
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            _ = ex; // observed; see comment above

            return;
        }

        context.Response.ContentType = ContentTypes.Applications.Json;
        context.Response.ContentLength = buffer.WrittenCount;

        // Copy to the response body. Client disconnect here is expected and non-fatal, so we use
        // CancellationToken.None to avoid propagating RequestAborted as a 500.
        await context.Response.Body.WriteAsync(buffer.WrittenMemory, CancellationToken.None).ConfigureAwait(false);
    }
}
