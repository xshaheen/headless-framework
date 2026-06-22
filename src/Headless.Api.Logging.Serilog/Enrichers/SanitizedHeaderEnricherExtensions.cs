// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Configuration;

namespace Headless.Logging.Enrichers;

[PublicAPI]
public static class SanitizedHeaderEnricherExtensions
{
    private const int _DefaultMaxLength = 512;

    /// <summary>
    /// Enriches log events with a sanitized HTTP request header value. Sanitization removes
    /// newline characters (log-injection prevention), ANSI escape sequences, and other control
    /// characters, then truncates the result to <paramref name="maxLength"/> characters.
    /// </summary>
    /// <param name="enrichmentConfiguration">Logger enrichment configuration.</param>
    /// <param name="headerName">The name of the HTTP request header to read.</param>
    /// <param name="propertyName">
    /// The Serilog property name. Defaults to <paramref name="headerName"/> with dashes removed
    /// (e.g. <c>"User-Agent"</c> becomes <c>"UserAgent"</c>).
    /// </param>
    /// <param name="maxLength">Maximum character length of the sanitized value. Default is 512.</param>
    /// <returns><paramref name="enrichmentConfiguration"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="enrichmentConfiguration"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="headerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="headerName"/> is empty or whitespace.</exception>
    public static LoggerConfiguration WithSanitizedRequestHeader(
        this LoggerEnrichmentConfiguration enrichmentConfiguration,
        string headerName,
        string? propertyName = null,
        int maxLength = _DefaultMaxLength
    )
    {
        Argument.IsNotNull(enrichmentConfiguration);
        Argument.IsNotNullOrWhiteSpace(headerName);

        var contextAccessor = new HttpContextAccessor();
        var enricher = new SanitizedHeaderEnricher(contextAccessor, headerName, propertyName, maxLength);

        return enrichmentConfiguration.With(enricher);
    }

    /// <summary>
    /// Enriches log events with a sanitized HTTP request header value, resolving
    /// <c>IHttpContextAccessor</c> from <paramref name="serviceProvider"/> instead of creating a
    /// new instance. Prefer this overload when the application's DI container already manages the accessor.
    /// </summary>
    /// <param name="enrichmentConfiguration">Logger enrichment configuration.</param>
    /// <param name="serviceProvider">Service provider used to resolve <c>IHttpContextAccessor</c>.</param>
    /// <param name="headerName">The name of the HTTP request header to read.</param>
    /// <param name="propertyName">
    /// The Serilog property name. Defaults to <paramref name="headerName"/> with dashes removed.
    /// </param>
    /// <param name="maxLength">Maximum character length of the sanitized value. Default is 512.</param>
    /// <returns><paramref name="enrichmentConfiguration"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="enrichmentConfiguration"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="headerName"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="headerName"/> is empty or whitespace.</exception>
    public static LoggerConfiguration WithSanitizedRequestHeader(
        this LoggerEnrichmentConfiguration enrichmentConfiguration,
        IServiceProvider serviceProvider,
        string headerName,
        string? propertyName = null,
        int maxLength = _DefaultMaxLength
    )
    {
        Argument.IsNotNull(enrichmentConfiguration);
        Argument.IsNotNull(serviceProvider);
        Argument.IsNotNullOrWhiteSpace(headerName);

        var contextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
        var enricher = new SanitizedHeaderEnricher(contextAccessor, headerName, propertyName, maxLength);

        return enrichmentConfiguration.With(enricher);
    }
}
