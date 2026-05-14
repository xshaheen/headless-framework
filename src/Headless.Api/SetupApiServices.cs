// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.IO.Compression;
using Headless.Abstractions;
using Headless.Api.Abstractions;
using Headless.Constants;
using Headless.Serializer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Headless.Api;

public static class SetupApiServices
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddHeadlessJsonService()
        {
            services.TryAddSingleton<IJsonOptionsProvider>(new DefaultJsonOptionsProvider());
            services.TryAddSingleton<IJsonSerializer>(sp => new SystemJsonSerializer(
                sp.GetRequiredService<IJsonOptionsProvider>()
            ));
            services.TryAddSingleton<ITextSerializer>(x => x.GetRequiredService<IJsonSerializer>());
            services.TryAddSingleton<ISerializer>(x => x.GetRequiredService<IJsonSerializer>());

            return services;
        }

        public IServiceCollection AddHeadlessTimeService()
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IClock, Clock>();
            services.TryAddSingleton<ITimezoneProvider, TzConvertTimezoneProvider>();

            return services;
        }

        public IServiceCollection AddHeadlessProblemDetails()
        {
            services.TryAddSingleton<IProblemDetailsCreator, ProblemDetailsCreator>();
            services.AddProblemDetails();

            // Resolve IProblemDetailsCreator lazily on each request rather than at options-build
            // time. ProblemDetailsCreator depends on IOptions<ProblemDetailsOptions>; capturing
            // the singleton via Configure<TDep>() at build time would create a circular DI
            // dependency that deadlocks during host startup.
            services.Configure<Microsoft.AspNetCore.Http.ProblemDetailsOptions>(options =>
            {
                options.CustomizeProblemDetails += context =>
                {
                    var creator = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsCreator>();
                    creator.Normalize(context.ProblemDetails);
                };
            });

            // Single IExceptionHandler covers framework-known exceptions (tenancy, conflict, validation,
            // not-found, EF concurrency, timeout, not-implemented, cancellation) for MVC actions,
            // Minimal-API endpoints, middleware, hosted services, and hubs. ASP.NET Core's
            // AddExceptionHandler<T>() uses plain AddSingleton which is not idempotent; using
            // TryAddEnumerable directly collapses duplicate registrations to a single descriptor.
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IExceptionHandler, Middlewares.HeadlessApiExceptionHandler>()
            );

            return services;
        }

        public IServiceCollection AddHeadlessApiResponseCompression()
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

        public IServiceCollection ConfigureHeadlessDefaultApi(string aliveTag = "live")
        {
            services.Configure<KestrelServerOptions>(options =>
            {
                options.AddServerHeader = false;
                options.Limits.MaxRequestBodySize = 1024 * 1024 * 30; // 30MB
                options.Limits.MaxRequestHeaderCount = 40;
            });

            services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), tags: [aliveTag]);

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
    }
}
