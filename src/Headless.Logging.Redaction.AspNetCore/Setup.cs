// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Checks;
using Headless.Logging.Internal;
using Microsoft.AspNetCore.Diagnostics.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Net.Http.Headers;
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
        /// request path: the request scope and request lines (through <c>AddHeadlessLogRedaction</c>), the HTTP
        /// logging record, and the server span's <c>url.path</c>, <c>http.target</c>, and <c>url.full</c> tags.
        /// </summary>
        /// <remarks>
        /// Idempotent. Like <c>AddHeadlessLogRedaction</c>, call it again after a later registration of a logger
        /// factory, such as <c>UseSerilog</c>. Spans other than the request span, such as an outgoing call to a
        /// credential URL, need <c>AddHeadlessLogPathRedaction</c> on the tracer provider.
        /// </remarks>
        /// <returns>The same <see cref="IServiceCollection"/> instance.</returns>
        public IServiceCollection AddHeadlessRequestLogRedaction()
        {
            Argument.IsNotNull(services);

            services.AddHeadlessLogRedaction();
            services.AddHttpLoggingInterceptor<LogPathRedactionHttpLoggingInterceptor>();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IStartupFilter, LogPathTraceRedactionStartupFilter>()
            );

            return services;
        }

        /// <summary>
        /// Registers Microsoft's classification-based HTTP logging redaction (<c>AddHttpLoggingRedaction</c>) with the
        /// credential headers mapped to <see cref="HeadlessDataClassifications.Secret"/>, and
        /// <see cref="AddHeadlessRequestLogRedaction"/> for declared credential paths.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>Authorization</c>, <c>Proxy-Authorization</c>, and <c>Cookie</c> on the request and <c>Set-Cookie</c> on
        /// the response are logged by name with the value erased. Microsoft's middleware logs no header you do not
        /// classify, so map any other header you want to see, and classify route parameters that carry personal data
        /// or identifiers, in <paramref name="configure"/>.
        /// </para>
        /// <para>
        /// Microsoft marks the underlying API experimental, so this method carries the same <c>EXTEXP0013</c>
        /// diagnostic; suppress it where you call it. It registers HTTP logging itself; the pipeline still needs <c>app.UseHttpLogging()</c>. Register the
        /// redactors with <c>AddHeadlessRedaction</c>. Idempotent: a second call only runs its
        /// <paramref name="configure"/>.
        /// </para>
        /// </remarks>
        /// <param name="configure">Runs after the defaults, so it can add classifications or replace a default.</param>
        /// <returns>The same <see cref="IServiceCollection"/> instance.</returns>
        [Experimental("EXTEXP0013", UrlFormat = "https://aka.ms/dotnet-extensions-warnings/{0}")]
        public IServiceCollection AddHeadlessHttpLoggingRedaction(Action<LoggingRedactionOptions>? configure = null)
        {
            Argument.IsNotNull(services);

            services.AddHeadlessRequestLogRedaction();

            if (services.Any(static d => d.ServiceType == typeof(HttpLoggingRedactionMarker)))
            {
                if (configure is not null)
                {
                    services.Configure(configure);
                }

                return services;
            }

            services.AddSingleton<HttpLoggingRedactionMarker>();
            services.AddHttpLoggingRedaction(options =>
            {
                foreach (var header in _CredentialRequestHeaders)
                {
                    options.RequestHeadersDataClasses[header] = HeadlessDataClassifications.Secret;
                }

                options.ResponseHeadersDataClasses[HeaderNames.SetCookie] = HeadlessDataClassifications.Secret;
                configure?.Invoke(options);
            });

            return services;
        }
    }

    private static readonly string[] _CredentialRequestHeaders =
    [
        HeaderNames.Authorization,
        HeaderNames.ProxyAuthorization,
        HeaderNames.Cookie,
    ];

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
