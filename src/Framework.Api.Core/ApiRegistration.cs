using System.Diagnostics;
using FileSignatures;
using FluentValidation;
using Framework.Api.Core.Abstractions;
using Framework.Api.Core.Diagnostics;
using Framework.BuildingBlocks.Abstractions;
using Framework.BuildingBlocks.Constants;
using Framework.BuildingBlocks.Helpers;
using Microsoft.AspNetCore.Builder;
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
        builder.Services.AddResilienceEnricher();

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

    public static void ConfigureGlobalSettings()
    {
        AppDomain.CurrentDomain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(1));
        ValidatorOptions.Global.LanguageManager.Enabled = true;
        ValidatorOptions.Global.DefaultRuleLevelCascadeMode = CascadeMode.Stop;
    }
}
