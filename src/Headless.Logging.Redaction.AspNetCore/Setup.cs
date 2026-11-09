// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Logging.Internal;
using Microsoft.AspNetCore.Diagnostics.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace Headless.Logging;

/// <summary>Registers credential-path redaction for ASP.NET Core's HTTP logging and for OpenTelemetry spans.</summary>
[PublicAPI]
[SuppressMessage(
    "Naming",
    "CA1708:Identifiers should differ by more than case",
    Justification = "C# 14 extension member blocks emit compiler-generated marker members differing only by case."
)]
public static class SetupLoggingRedactionAspNetCore
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Replaces every path an <see cref="ILogPathRedactor"/> recognizes in each place ASP.NET Core writes the
        /// request path: the request scope and request lines (through <c>AddHeadlessRedaction</c>), the HTTP
        /// logging record, and the server span's <c>url.path</c>, <c>http.target</c>, and <c>url.full</c> tags.
        /// </summary>
        /// <remarks>
        /// Idempotent, and safe to call from libraries. Like <c>AddHeadlessRedaction</c>, it wraps the logger factories
        /// registered so far; <c>AddHeadlessSerilog</c> wraps its own. Spans other than the request span, such as an outgoing call to a
        /// credential URL, need <c>AddHeadlessLogPathRedaction</c> on the tracer provider.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection"/> instance.</returns>
        public IServiceCollection AddHeadlessRequestLogRedaction()
        {
            Argument.IsNotNull(services);

            services.AddHeadlessRedaction();
            services.AddHttpLoggingInterceptor<LogPathRedactionHttpLoggingInterceptor>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IStartupFilter, LogPathTraceRedactionStartupFilter>()
            );

            return services;
        }

        /// <summary>
        /// Registers Microsoft's classification-based HTTP logging redaction with the credential headers mapped to
        /// <see cref="HeadlessDataClassifications.Secret"/>, and <see cref="AddHeadlessRequestLogRedaction"/> for
        /// declared credential paths.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>Authorization</c>, <c>Proxy-Authorization</c>, and <c>Cookie</c> on the request and <c>Set-Cookie</c> on
        /// the response are logged by name with the value erased. HTTP logging writes no header you do not list, so add
        /// any other header you want to see, and classify route parameters that carry personal data or identifiers,
        /// in <paramref name="configure"/>.
        /// </para>
        /// <para>
        /// It registers HTTP logging itself; the pipeline still needs <c>app.UseHttpLogging()</c>. Idempotent: a second
        /// call only adds its <paramref name="configure"/>, which runs after the earlier ones.
        /// </para>
        /// </remarks>
        /// <param name="configure">Runs after the defaults, so it can add entries or replace a default.</param>
        /// <returns>The same <see cref="IServiceCollection"/> instance.</returns>
        public IServiceCollection AddHeadlessHttpLoggingRedaction(Action<HttpLoggingRedactionOptions>? configure = null)
        {
            Argument.IsNotNull(services);

            services.AddHeadlessRequestLogRedaction();

            if (configure is not null)
            {
                services.Configure(configure);
            }

            if (services.Any(static d => d.ServiceType == typeof(HttpLoggingRedactionMarker)))
            {
                return services;
            }

            services.AddSingleton<HttpLoggingRedactionMarker>();

#pragma warning disable EXTEXP0013 // Microsoft marks the API experimental; this method maps onto it so callers never see the diagnostic.
            services.AddHttpLoggingRedaction();
            services.AddSingleton<IConfigureOptions<LoggingRedactionOptions>, HttpLoggingRedactionOptionsSetup>();
#pragma warning restore EXTEXP0013

            return services;
        }
    }

    // Marks that the defaults ran, so a second call adds its own callback without registering the middleware again.
    private sealed class HttpLoggingRedactionMarker;

    extension(TracerProviderBuilder builder)
    {
        /// <summary>
        /// Adds a processor that replaces every path an <see cref="ILogPathRedactor"/> recognizes in the path tags
        /// (<c>url.path</c>, <c>http.target</c>, <c>url.full</c>) of every span, server and client, when it ends. The
        /// query and fragment of a matching value are dropped.
        /// </summary>
        /// <remarks>
        /// Processors run in the order they are added, so add this one before any exporter: an exporter added first
        /// exports the span before the path is replaced.
        /// </remarks>
        /// <returns>The same <see cref="TracerProviderBuilder"/> instance.</returns>
        public TracerProviderBuilder AddHeadlessLogPathRedaction()
        {
            Argument.IsNotNull(builder);

            return builder.AddProcessor(static provider => new LogPathRedactionActivityProcessor([
                .. provider.GetServices<ILogPathRedactor>(),
            ]));
        }
    }
}
