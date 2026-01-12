// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Configuration;

namespace Framework.Logging.Enrichers;

[PublicAPI]
public static class SanitizedHeaderEnricherExtensions
{
    private const int _DefaultMaxLength = 512;

    /// <summary>
    /// Enrich log events with a sanitized HTTP request header value.
    /// Sanitization removes newlines, ANSI escape sequences, and truncates to max length.
    /// </summary>
    /// <param name="enrichmentConfiguration">Logger enrichment configuration.</param>
    /// <param name="headerName">The name of the HTTP header to enrich with.</param>
    /// <param name="propertyName">Optional property name. Defaults to header name without dashes.</param>
    /// <param name="maxLength">Maximum length of the sanitized value. Default is 512.</param>
    /// <returns>Configuration object allowing method chaining.</returns>
    public static LoggerConfiguration WithSanitizedRequestHeader(
        this LoggerEnrichmentConfiguration enrichmentConfiguration,
        string headerName,
        string? propertyName = null,
        int maxLength = _DefaultMaxLength)
    {
        ArgumentNullException.ThrowIfNull(enrichmentConfiguration);
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);

        var contextAccessor = new HttpContextAccessor();
        var enricher = new SanitizedHeaderEnricher(contextAccessor, headerName, propertyName, maxLength);

        return enrichmentConfiguration.With(enricher);
    }

    /// <summary>
    /// Enrich log events with a sanitized HTTP request header value using a service provider.
    /// </summary>
    /// <param name="enrichmentConfiguration">Logger enrichment configuration.</param>
    /// <param name="serviceProvider">Service provider to resolve IHttpContextAccessor.</param>
    /// <param name="headerName">The name of the HTTP header to enrich with.</param>
    /// <param name="propertyName">Optional property name. Defaults to header name without dashes.</param>
    /// <param name="maxLength">Maximum length of the sanitized value. Default is 512.</param>
    /// <returns>Configuration object allowing method chaining.</returns>
    public static LoggerConfiguration WithSanitizedRequestHeader(
        this LoggerEnrichmentConfiguration enrichmentConfiguration,
        IServiceProvider serviceProvider,
        string headerName,
        string? propertyName = null,
        int maxLength = _DefaultMaxLength)
    {
        ArgumentNullException.ThrowIfNull(enrichmentConfiguration);
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(headerName);

        var contextAccessor = serviceProvider.GetRequiredService<IHttpContextAccessor>();
        var enricher = new SanitizedHeaderEnricher(contextAccessor, headerName, propertyName, maxLength);

        return enrichmentConfiguration.With(enricher);
    }
}
