// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.IO.Compression;
using FileSignatures;
using FluentValidation;
using Framework.Abstractions;
using Framework.Api.Abstractions;
using Framework.Api.Diagnostics;
using Framework.Api.Identity.Normalizer;
using Framework.Api.Identity.Schemes;
using Framework.Api.Security.Claims;
using Framework.Api.Security.Jwt;
using Framework.Constants;
using Framework.Core;
using Framework.Serializer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace Framework.Api;

[PublicAPI]
public static class ApiSetup
{
    public static readonly FileFormatInspector FileFormatInspector = new(FileFormatLocator.GetFormats());

    public static void ConfigureGlobalSettings()
    {
        AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(1));
        ValidatorOptions.Global.LanguageManager.Enabled = true;
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;
        JsonWebTokenHandler.DefaultMapInboundClaims = false;
        JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();
    }

    public static WebApplicationBuilder AddHeadlessApi(
        this WebApplicationBuilder builder,
        Action<StringEncryptionOptions> configureEncryption
    )
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddResilienceEnricher();
        builder.Services.AddHeadlessJsonService();
        builder.Services.AddHeadlessTimeService();
        builder.Services.AddHeadlessStringHashService();
        builder.Services.AddHeadlessStringEncryptionService(configureEncryption);
        builder.Services.AddHeadlessApiResponseCompression();
        builder.Services.AddHeadlessProblemDetails();
        builder.Services.AddHeadlessApiConfigurations();
        builder.Services.ConfigureHeadlessHstsOptions();

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
        builder.Services.TryAddSingleton<ICurrentTenant, NullCurrentTenant>();
        builder.Services.TryAddSingleton<IWebClientInfoProvider, HttpWebClientInfoProvider>();

        builder.Services.TryAddScoped<IRequestContext, HttpRequestContext>();
        builder.Services.TryAddScoped<IAbsoluteUrlFactory, HttpAbsoluteUrlFactory>();
        builder.Services.TryAddScoped<IRequestedApiVersion, HttpContextRequestedApiVersion>();

        builder.Services.AddOrReplaceSingleton<ILookupNormalizer, HeadlessLookupNormalizer>();
        builder.Services.AddOrReplaceSingleton<IAuthenticationSchemeProvider, DynamicAuthenticationSchemeProvider>();

        // Turn on resilience by default
        builder.Services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());

        return builder;
    }

    public static IServiceCollection AddHeadlessJsonService(this IServiceCollection services)
    {
        services.TryAddSingleton<IJsonOptionsProvider>(new DefaultJsonOptionsProvider());
        services.TryAddSingleton<IJsonSerializer>(sp => new SystemJsonSerializer(
            sp.GetRequiredService<IJsonOptionsProvider>()
        ));
        services.TryAddSingleton<ITextSerializer>(x => x.GetRequiredService<IJsonSerializer>());
        services.TryAddSingleton<ISerializer>(x => x.GetRequiredService<IJsonSerializer>());

        return services;
    }

    public static IServiceCollection AddHeadlessTimeService(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClock, Clock>();
        services.TryAddSingleton<ITimezoneProvider, TzConvertTimezoneProvider>();

        return services;
    }

    public static IServiceCollection AddHeadlessStringHashService(this IServiceCollection services)
    {
        services.AddOptions<StringHashOptions, StringHashOptionsValidator>();
        services.AddSingletonOptionValue<StringHashOptions>();
        services.TryAddSingleton<IStringHashService, StringHashService>();

        return services;
    }

    public static IServiceCollection AddHeadlessStringEncryptionService(
        this IServiceCollection services,
        Action<StringEncryptionOptions> configure
    )
    {
        services.Configure<StringEncryptionOptions, StringEncryptionOptionsValidator>(configure);
        services.AddSingletonOptionValue<StringEncryptionOptions>();
        services.TryAddSingleton<IStringEncryptionService, StringEncryptionService>();

        return services;
    }

    public static IServiceCollection AddHeadlessProblemDetails(this IServiceCollection services)
    {
        services.TryAddSingleton<IProblemDetailsCreator, ProblemDetailsCreator>();

        services.AddProblemDetails(options =>
        {
            options.CustomizeProblemDetails += context =>
            {
                var normalizer = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>();
                normalizer.Normalize(context.ProblemDetails);
            };
        });

        return services;
    }

    public static IServiceCollection AddHeadlessApiResponseCompression(this IServiceCollection services)
    {
        services
            .AddResponseCompression(options =>
            {
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                    [ContentTypes.Applications.ProblemJson, ContentTypes.Images.SvgXml, ContentTypes.Images.Icon]
                );

                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
            })
            .Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest)
            .Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

        return services;
    }

    public static IServiceCollection AddHeadlessApiConfigurations(this IServiceCollection services)
    {
        services.Configure<RouteOptions>(options =>
        {
            options.LowercaseUrls = true;
            options.AppendTrailingSlash = false;
        });

        services.Configure<FormOptions>(options =>
        {
            options.ValueLengthLimit = 1024 * 1024 * 4; // 4MB
            options.MultipartHeadersLengthLimit = 1024 * 16; // 16KB
            options.MultipartBodyLengthLimit = 1024 * 1024 * 30; // 30 MB
        });

        return services;
    }

    public static IServiceCollection ConfigureHeadlessHstsOptions(this IServiceCollection services)
    {
        /*
         * Configures the Strict-Transport-Security HTTP header on responses. This HTTP header is only relevant if you are
         * using TLS. It ensures that content is loaded over HTTPS and refuses to connect in case of certificate errors and
         * warnings. See https://developer.mozilla.org/en-US/docs/Web/Security/HTTP_strict_transport_security and
         * http://www.troyhunt.com/2015/06/understanding-http-strict-transport.html
         * Note: Including subdomains and a minimum maxage of 18 weeks is required for preloading.
         * Note: You can refer to the following article to clear the HSTS cache in your browser
         * http://classically.me/blogs/how-clear-hsts-settings-major-browsers.
         */
        services.Configure<HstsOptions>(options =>
        {
            // Preload the HSTS HTTP header for better security. See https://hstspreload.org/
            options.IncludeSubDomains = true;
            options.Preload = true;
            options.MaxAge = TimeSpan.FromDays(365);
        });

        return services;
    }

    extension(WebApplication app)
    {
        [MustDisposeResource]
        public IDisposable AddApiBadRequestDiagnosticListeners()
        {
            var diagnosticListener = app.Services.GetRequiredService<DiagnosticListener>();
            var badRequest = new BadRequestDiagnosticAdapter(app.Logger);
            var badRequestSubscription = diagnosticListener.SubscribeWithAdapter(badRequest);

            return badRequestSubscription;
        }

        [MustDisposeResource]
        public IDisposable AddMiddlewareAnalysisDiagnosticListeners()
        {
            var diagnosticListener = app.Services.GetRequiredService<DiagnosticListener>();
            var middlewareAnalysis = new MiddlewareAnalysisDiagnosticAdapter(app.Logger);
            var middlewareAnalysisSubscription = diagnosticListener.SubscribeWithAdapter(middlewareAnalysis);

            return middlewareAnalysisSubscription;
        }

        [MustDisposeResource]
        public IDisposable AddHeadlessApiDiagnosticListeners()
        {
            var diagnosticListener = app.Services.GetRequiredService<DiagnosticListener>();

            var badRequest = new BadRequestDiagnosticAdapter(app.Logger);
            var badRequestSubscription = diagnosticListener.SubscribeWithAdapter(badRequest);

            var middlewareAnalysis = new MiddlewareAnalysisDiagnosticAdapter(app.Logger);
            var middlewareAnalysisSubscription = diagnosticListener.SubscribeWithAdapter(middlewareAnalysis);

            return DisposableFactory.Create(() =>
            {
                badRequestSubscription.Dispose();
                middlewareAnalysisSubscription.Dispose();
            });
        }
    }
}
