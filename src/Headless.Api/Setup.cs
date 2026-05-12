// Copyright (c) Mahmoud Shaheen. All rights reserved.

using FileSignatures;
using FluentValidation;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Identity.Normalizer;
using Headless.Api.Identity.Schemes;
using Headless.Api.Middlewares;
using Headless.Api.Security.Claims;
using Headless.Api.Security.Jwt;
using Headless.Checks;
using Headless.Constants;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Headless.Api;

[PublicAPI]
public static class ApiSetup
{
    private const string _StringEncryptionSectionName = "Headless:StringEncryption";
    private const string _StringHashSectionName = "Headless:StringHash";

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
        public WebApplicationBuilder AddHeadlessInfrastructure()
        {
            Argument.IsNotNull(builder);

            builder._AddDefaultStringEncryptionService();
            builder._AddDefaultStringHashService();

            return builder._AddCore();
        }

        public WebApplicationBuilder AddHeadlessInfrastructure(
            IConfiguration stringEncryptionConfig,
            IConfiguration stringHashConfig
        )
        {
            Argument.IsNotNull(builder);
            Argument.IsNotNull(stringEncryptionConfig);
            Argument.IsNotNull(stringHashConfig);

            builder.Services.AddStringEncryptionService(stringEncryptionConfig);
            builder.Services.AddStringHashService(stringHashConfig);

            return builder._AddCore();
        }

        public WebApplicationBuilder AddHeadlessInfrastructure(
            Action<StringEncryptionOptions> configureEncryption,
            Action<StringHashOptions>? configureHash = null
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

            return builder._AddCore();
        }

        public WebApplicationBuilder AddHeadlessInfrastructure(
            Action<StringEncryptionOptions, IServiceProvider> configureEncryption,
            Action<StringHashOptions, IServiceProvider>? configureHash = null
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

            return builder._AddCore();
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

        private WebApplicationBuilder _AddCore()
        {
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddHttpContextAccessor();
            builder.Services.AddResilienceEnricher();
            builder.Services.AddHeadlessJsonService();
            builder.Services.AddHeadlessTimeService();
            builder.Services.AddHeadlessApiResponseCompression();
            builder.Services.AddHeadlessProblemDetails();
            builder.Services.AddStatusCodesRewriterMiddleware();
            builder.Services.ConfigureHeadlessDefaultApi();

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

            // Turn on resilience by default
            builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

            return builder;
        }
    }

    /// <summary>Applies the default Headless API middleware order.</summary>
    public static WebApplication UseHeadlessDefaults(
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
            app.UseExceptionHandler();
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

        return app;
    }

    /// <summary>Maps the default Headless API operational endpoints.</summary>
    public static IEndpointRouteBuilder MapHeadlessDefaultEndpoints(
        this IEndpointRouteBuilder endpoints,
        Action<HeadlessApiDefaultEndpointOptions>? configure = null
    )
    {
        Argument.IsNotNull(endpoints);

        var options = new HeadlessApiDefaultEndpointOptions();
        configure?.Invoke(options);

        if (options.MapHealthEndpoint)
        {
            var healthChecks = endpoints.MapHealthChecks(
                options.HealthPath,
                new HealthCheckOptions { ResponseWriter = options.HealthResponseWriter }
            );

            _ConfigureOperationalEndpoint(healthChecks, options.HealthEndpointName, options);
        }

        if (options.MapAliveEndpoint)
        {
            var aliveCheck = endpoints.MapHealthChecks(
                options.AlivePath,
                new HealthCheckOptions { Predicate = registration => registration.Tags.Contains(options.AliveTag) }
            );

            _ConfigureOperationalEndpoint(aliveCheck, options.AliveEndpointName, options);
        }

        return endpoints;
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
