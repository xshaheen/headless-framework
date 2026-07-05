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

/// <summary>
/// Extension members on <see cref="IServiceCollection"/> for registering core Headless API
/// infrastructure — JSON serialization, time services, ProblemDetails, compression, and common
/// Kestrel / routing / form defaults.
/// </summary>
[PublicAPI]
public static class SetupApiServices
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers the Headless JSON stack: <see cref="IJsonOptionsProvider"/> (default options),
        /// <see cref="IJsonSerializer"/> (System.Text.Json), and the <see cref="ITextSerializer"/>
        /// and <see cref="ISerializer"/> adapters that delegate to it.
        /// All registrations are guarded with <c>TryAddSingleton</c> so consumers can supply their
        /// own implementations before calling this method.
        /// </summary>
        /// <returns>The same service collection.</returns>
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

        /// <summary>
        /// Registers the Headless time stack: <see cref="TimeProvider.System"/>, <see cref="IClock"/>,
        /// and <see cref="ITimezoneProvider"/> (TzConvert-backed).
        /// All registrations are guarded with <c>TryAddSingleton</c>.
        /// </summary>
        /// <returns>The same service collection.</returns>
        public IServiceCollection AddHeadlessTimeService()
        {
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<IClock, Clock>();
            services.TryAddSingleton<ITimezoneProvider, TzConvertTimezoneProvider>();

            return services;
        }

        /// <summary>
        /// Registers <see cref="IProblemDetailsCreator"/>, ASP.NET Core's
        /// <see cref="Microsoft.AspNetCore.Http.IProblemDetailsService"/> with a
        /// <c>CustomizeProblemDetails</c> hook that normalizes the response through the creator, and
        /// <see cref="Middlewares.HeadlessApiExceptionHandler"/> as a singleton
        /// <see cref="Microsoft.AspNetCore.Diagnostics.IExceptionHandler"/>.
        /// </summary>
        /// <remarks>
        /// The exception handler maps the following exception types to HTTP status codes:
        /// <list type="bullet">
        /// <item><description><see cref="Headless.Exceptions.UnauthorizedException"/> → 401</description></item>
        /// <item><description><see cref="Headless.Exceptions.ConflictException"/> → 409</description></item>
        /// <item><description><see cref="Headless.Exceptions.EntityNotFoundException"/> → 404</description></item>
        /// <item><description><see cref="Headless.Abstractions.CrossTenantWriteException"/> → 409 with <c>g:cross_tenant_write</c></description></item>
        /// <item><description><see cref="Headless.Abstractions.MissingTenantContextException"/> → 403 with <c>g:tenant_required</c></description></item>
        /// <item><description><c>FluentValidation.ValidationException</c> → 422</description></item>
        /// <item><description><c>Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException</c> (matched by name) → 409</description></item>
        /// <item><description><see cref="TimeoutException"/> → 408</description></item>
        /// <item><description><see cref="NotImplementedException"/> → 501</description></item>
        /// <item><description>Client-cancelled (<see cref="OperationCanceledException"/> with <c>RequestAborted</c>) → 499</description></item>
        /// </list>
        /// All other exceptions return <see langword="false"/> and are left to subsequent handlers.
        /// </remarks>
        /// <returns>The same service collection.</returns>
        public IServiceCollection AddHeadlessProblemDetails()
        {
            services.TryAddSingleton<IProblemDetailsCreator, ProblemDetailsCreator>();
            services.AddProblemDetails();

            // Resolve IProblemDetailsCreator from request services so consumer replacements are
            // honored at write time instead of capturing the default singleton at options-build time.
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

        /// <summary>
        /// Adds response compression with Brotli and Gzip providers at <c>Fastest</c> level,
        /// and extends the default MIME-type list with <c>application/problem+json</c>,
        /// <c>image/svg+xml</c>, and <c>image/x-icon</c>.
        /// </summary>
        /// <returns>The same service collection.</returns>
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

        /// <summary>
        /// Applies Headless-standard defaults to Kestrel, routing, form options, and HSTS:
        /// <list type="bullet">
        /// <item><description>Kestrel: no <c>Server</c> header, 30 MB request body limit, 40-header max.</description></item>
        /// <item><description>Health check: a self-check tagged with <paramref name="aliveTag"/> for liveness probes.</description></item>
        /// <item><description>Routing: lowercase URLs, no trailing slash.</description></item>
        /// <item><description>Form: 4 MB value limit, 16 KB multipart-headers limit, 30 MB multipart-body limit.</description></item>
        /// <item><description>HSTS: 365-day max-age, subdomain inclusion, preload enabled.</description></item>
        /// </list>
        /// </summary>
        /// <param name="aliveTag">Tag applied to the self health check (used by liveness probes). Defaults to <c>live</c>.</param>
        /// <returns>The same service collection.</returns>
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
