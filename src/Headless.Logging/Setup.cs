// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Logging.Internal;
using Headless.Reflection;
using Microsoft.Extensions.AmbientMetadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

namespace Headless.Logging;

/// <summary>Registers the Headless logging profile.</summary>
[PublicAPI]
public static class SetupLogging
{
    /// <summary>The timestamp every Headless console line starts with, in UTC.</summary>
    internal const string TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z' ";

    extension(IHostApplicationBuilder builder)
    {
        /// <summary>
        /// Gives the host the Headless logging profile: an environment-aware console, enrichment on every entry, and the
        /// Headless redaction.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The console writes one JSON object per entry outside Development and one text line inside it, both with UTC
        /// timestamps and scopes. Every entry carries <c>deployment.environment</c>, <c>service.name</c>,
        /// <c>service.version</c>, <c>process.pid</c>, <c>thread.id</c>, <c>MachineName</c>, <c>ProcessName</c>,
        /// <c>CommitHash</c>, and the trace and span IDs of the current span. An entry written while a message consumer
        /// or a job runs also carries <c>CausationTraceId</c>, the trace id of the span that published or scheduled the
        /// work. An entry written while a tenant is ambient carries <c>TenantId</c> (<c>TenantTelemetryOptions</c> renames
        /// or turns it off) once, also when its template already has a <c>{TenantId}</c> hole. Levels come from
        /// <c>Logging:LogLevel</c> and reload at run time.
        /// </para>
        /// <para>
        /// Enrichment uses Microsoft's log enrichment, so an enricher of your own goes in with
        /// <c>services.AddLogEnricher&lt;T&gt;()</c>. Every enricher's values pass through the property names classified
        /// with <c>ClassifyProperty</c>, whenever it was registered.
        /// </para>
        /// </remarks>
        /// <param name="options">The console format and header length; the defaults when omitted.</param>
        /// <returns>The same builder.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="builder"/> is <see langword="null"/>.</exception>
        public IHostApplicationBuilder AddHeadlessLogging(HeadlessLoggingOptions? options = null)
        {
            Argument.IsNotNull(builder);

            options ??= new HeadlessLoggingOptions();
            builder.Services.TryAddSingleton(options);

            builder.Logging.AddHeadlessConsole(options.ResolveConsoleFormat(builder.Environment.EnvironmentName));
            // A default host binds the section already; a host built without defaults would otherwise ignore it. The
            // binding reloads, so a changed level applies without a restart.
            builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
            builder.Logging.Configure(factory =>
                factory.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId
            );

            builder.Logging.EnableEnrichment();
            builder.Services.AddProcessLogEnricher(process =>
            {
                process.ProcessId = true;
                process.ThreadId = true;
            });

            var environment = builder.Environment;
            builder.Services.AddApplicationMetadata(metadata =>
            {
                // The metadata defaults to empty strings; a value bound from configuration wins.
                if (string.IsNullOrEmpty(metadata.ApplicationName))
                {
                    metadata.ApplicationName = environment.ApplicationName;
                }

                if (string.IsNullOrEmpty(metadata.EnvironmentName))
                {
                    metadata.EnvironmentName = environment.EnvironmentName;
                }

                if (string.IsNullOrEmpty(metadata.BuildVersion))
                {
                    metadata.BuildVersion = AssemblyInformation.Entry?.Version;
                }
            });
            builder.Services.AddApplicationLogEnricher(application =>
            {
                application.ApplicationName = true;
                application.EnvironmentName = true;
                application.BuildVersion = true;
            });
            builder.Services.AddStaticLogEnricher<HostLogEnricher>();
            builder.Services.AddLogEnricher<CausationLogEnricher>();
            builder.Services.AddLogEnricher<TenantLogEnricher>();

            // Last, so the redacting factory wraps Microsoft's enriching one.
            builder.Services.AddHeadlessRedaction();

            return builder;
        }
    }

    internal static ILoggingBuilder AddHeadlessConsole(this ILoggingBuilder logging, HeadlessConsoleFormat format)
    {
        if (format is HeadlessConsoleFormat.Json)
        {
            return logging.AddJsonConsole(console =>
            {
                console.IncludeScopes = true;
                console.UseUtcTimestamp = true;
                console.TimestampFormat = TimestampFormat.TrimEnd();
            });
        }

        return logging.AddSimpleConsole(console =>
        {
            console.IncludeScopes = true;
            console.SingleLine = true;
            console.UseUtcTimestamp = true;
            console.TimestampFormat = TimestampFormat;
            console.ColorBehavior = LoggerColorBehavior.Default;
        });
    }
}
