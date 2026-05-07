// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.IO.Compression;
using FileSignatures;
using FluentValidation;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Api.Diagnostics;
using Headless.Api.Identity.Normalizer;
using Headless.Api.Identity.Schemes;
using Headless.Api.Security.Claims;
using Headless.Api.Security.Jwt;
using Headless.Checks;
using Headless.Constants;
using Headless.Core;
using Headless.Serializer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

    public static WebApplicationBuilder AddHeadless(this WebApplicationBuilder builder)
    {
        Argument.IsNotNull(builder);

        _AddDefaultStringEncryptionService(builder);
        _AddDefaultStringHashService(builder);

        return _AddCore(builder);
    }

    public static WebApplicationBuilder AddHeadless(
        this WebApplicationBuilder builder,
        IConfiguration stringEncryptionConfig,
        IConfiguration stringHashConfig
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(stringEncryptionConfig);
        Argument.IsNotNull(stringHashConfig);

        builder.Services.AddStringEncryptionService(stringEncryptionConfig);
        builder.Services.AddStringHashService(stringHashConfig);

        return _AddCore(builder);
    }

    public static WebApplicationBuilder AddHeadless(
        this WebApplicationBuilder builder,
        Action<StringEncryptionOptions> configureEncryption,
        Action<StringHashOptions>? configureHash = null
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configureEncryption);

        builder.Services.AddStringEncryptionService(configureEncryption);

        if (configureHash is null)
        {
            _AddDefaultStringHashService(builder);
        }
        else
        {
            builder.Services.AddStringHashService(configureHash);
        }

        return _AddCore(builder);
    }

    public static WebApplicationBuilder AddHeadless(
        this WebApplicationBuilder builder,
        Action<StringEncryptionOptions, IServiceProvider> configureEncryption,
        Action<StringHashOptions, IServiceProvider>? configureHash = null
    )
    {
        Argument.IsNotNull(builder);
        Argument.IsNotNull(configureEncryption);

        builder.Services.AddStringEncryptionService(configureEncryption);

        if (configureHash is null)
        {
            _AddDefaultStringHashService(builder);
        }
        else
        {
            builder.Services.AddStringHashService(configureHash);
        }

        return _AddCore(builder);
    }

    private static void _AddDefaultStringEncryptionService(WebApplicationBuilder builder)
    {
        builder.Services.AddStringEncryptionService(
            builder.Configuration.GetRequiredSection(_StringEncryptionSectionName)
        );
    }

    private static void _AddDefaultStringHashService(WebApplicationBuilder builder)
    {
        builder.Services.AddStringHashService(builder.Configuration.GetRequiredSection(_StringHashSectionName));
    }

    private static WebApplicationBuilder _AddCore(WebApplicationBuilder builder)
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddResilienceEnricher();
        builder.Services.AddJsonService();
        builder.Services.AddTimeService();
        builder.Services.AddApiResponseCompression();
        builder.Services.AddHeadlessProblemDetails();
        builder.Services.AddApiConfigurations();
        builder.Services.ConfigureHstsOptions();

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
        // AddOrReplace (not TryAdd) so the real ambient-tenant resolver always wins over
        // any fallback (e.g. Headless.Messaging.Core's NullCurrentTenant) regardless of
        // package registration order. Mirrors MultiTenancySetup.AddHeadlessMultiTenancy.
        builder.Services.AddOrReplaceSingleton<ICurrentTenant, CurrentTenant>();
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

    public static IServiceCollection AddJsonService(this IServiceCollection services)
    {
        services.TryAddSingleton<IJsonOptionsProvider>(new DefaultJsonOptionsProvider());
        services.TryAddSingleton<IJsonSerializer>(sp => new SystemJsonSerializer(
            sp.GetRequiredService<IJsonOptionsProvider>()
        ));
        services.TryAddSingleton<ITextSerializer>(x => x.GetRequiredService<IJsonSerializer>());
        services.TryAddSingleton<ISerializer>(x => x.GetRequiredService<IJsonSerializer>());

        return services;
    }

    public static IServiceCollection AddTimeService(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IClock, Clock>();
        services.TryAddSingleton<ITimezoneProvider, TzConvertTimezoneProvider>();

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

        // Backfill ApiBehaviorOptions.ClientErrorMapping for status codes the framework maps but
        // ASP.NET Core's defaults omit (408, 501). With these entries present, status-code-pages
        // bodies emitted for empty 408/501 responses (e.g., from RequestTimeoutsMiddleware) get the
        // same Title + Type the IProblemDetailsCreator factories produce — both paths converge on
        // one wire shape.
        services.Configure<ApiBehaviorOptions>(options =>
        {
            options.ClientErrorMapping[StatusCodes.Status408RequestTimeout] = new ClientErrorData
            {
                Title = HeadlessProblemDetailsConstants.Titles.RequestTimeout,
                Link = HeadlessProblemDetailsConstants.Types.RequestTimeout,
            };
            options.ClientErrorMapping[StatusCodes.Status501NotImplemented] = new ClientErrorData
            {
                Title = HeadlessProblemDetailsConstants.Titles.NotImplemented,
                Link = HeadlessProblemDetailsConstants.Types.NotImplemented,
            };
        });

        // Single IExceptionHandler covers framework-known exceptions (tenancy, conflict, validation,
        // not-found, EF concurrency, timeout, not-implemented, cancellation) for MVC actions,
        // Minimal-API endpoints, middleware, hosted services, and hubs. ASP.NET Core's
        // AddExceptionHandler<T>() uses plain AddSingleton which is not idempotent; using
        // TryAddEnumerable directly collapses duplicate registrations to a single descriptor.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IExceptionHandler, HeadlessApiExceptionHandler>());

        return services;
    }

    public static IServiceCollection AddApiResponseCompression(this IServiceCollection services)
    {
        services
            .AddResponseCompression(options =>
            {
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat([
                    ContentTypes.Applications.ProblemJson,
                    ContentTypes.Images.SvgXml,
                    ContentTypes.Images.Icon,
                ]);

                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
            })
            .Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest)
            .Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

        return services;
    }

    public static IServiceCollection AddApiConfigurations(this IServiceCollection services)
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

    public static IServiceCollection ConfigureHstsOptions(this IServiceCollection services)
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
