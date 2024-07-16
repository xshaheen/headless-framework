using System.Diagnostics;
using System.IO.Compression;
using FileSignatures;
using FluentValidation;
using Framework.Api.Core.Abstractions;
using Framework.Api.Core.Diagnostics;
using Framework.Api.Core.Middlewares;
using Framework.BuildingBlocks.Abstractions;
using Framework.BuildingBlocks.Constants;
using Framework.BuildingBlocks.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;

namespace Framework.Api.Core;

public static class ApiRegistration
{
    public static readonly FileFormatInspector FileFormatInspector = new(FileFormatLocator.GetFormats());

    public static readonly FileExtensionContentTypeProvider FileExtensionContentTypeProvider =
        new() { Mappings = { [".liquid"] = ContentTypes.Html, [".md"] = ContentTypes.Html, }, };

    public static void AddApiCore(this WebApplicationBuilder builder)
    {
        builder.Services.AddServerTimingMiddleware();
        builder.Services.AddCustomStatusCodesRewriterMiddleware();
        builder.Services.AddRequestCanceledMiddleware();

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddResilienceEnricher();
        builder.Services.AddApiCoreResponseCompression();
        builder.Services.AddApiCoreConfigurations();

        builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
        builder.Services.AddScoped<IAbsoluteUrlFactory, HttpAbsoluteUrlFactory>();
        builder.Services.AddScoped<IRequestTime, RequestTime>();

        builder.Services.AddSingleton<IFileFormatInspector>(FileFormatInspector);
        builder.Services.AddSingleton<IContentTypeProvider>(FileExtensionContentTypeProvider);

        builder.Services.AddSingleton<IProblemDetailsCreator, ProblemDetailsCreator>();
        builder.Services.AddSingleton<ICancellationTokenProvider, HttpContextCancellationTokenProvider>();
        builder.Services.AddSingleton<IClaimsPrincipalFactory, ClaimsPrincipalFactory>();
        builder.Services.AddSingleton<IJwtTokenFactory, JwtTokenFactory>();

        builder.Services.AddSingleton<IGuidGenerator, SequentialAtEndGuidGenerator>();
        builder.Services.AddSingleton<IUniqueLongGenerator>(new SnowFlakIdUniqueLongGenerator(1));
        builder.Services.AddSingleton<IClock, Clock>();
        builder.Services.AddSingleton<ITimezoneProvider, TzConvertTimezoneProvider>();
        builder.Services.AddSingleton<IHashService>(_ => new HashService(iterations: 10000, size: 128));
    }

    public static void UseApiCore(this WebApplication app)
    {
        app.UseSecurityHeaders();
    }

    public static IDisposable AddApiCoreDiagnosticListeners(this WebApplication app)
    {
        var diagnosticListener = app.Services.GetRequiredService<DiagnosticListener>();

        var badRequest = new BadRequestDiagnosticAdapter(app.Logger);
        var badRequestSubscription = diagnosticListener.SubscribeWithAdapter(badRequest);

        var middlewareAnalysis = new MiddlewareAnalysisDiagnosticAdapter(app.Logger);
        var middlewareAnalysisSubscription = diagnosticListener.SubscribeWithAdapter(middlewareAnalysis);

        return new DisposeAction(() =>
        {
            badRequestSubscription.Dispose();
            middlewareAnalysisSubscription.Dispose();
        });
    }

    public static void AddApiCoreResponseCompression(this IServiceCollection services)
    {
        services
            .AddResponseCompression(options =>
            {
                options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
                    [ContentTypes.ProblemJson, ContentTypes.Svg, ContentTypes.Icon]
                );

                options.Providers.Add<BrotliCompressionProvider>();
                options.Providers.Add<GzipCompressionProvider>();
            })
            .Configure<BrotliCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest)
            .Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);
    }

    public static void AddApiCoreConfigurations(this IServiceCollection services)
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
    }

    public static void ConfigureGlobalSettings()
    {
        AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(1));
        ValidatorOptions.Global.LanguageManager.Enabled = true;
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;
    }
}
