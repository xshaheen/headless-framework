// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Diagnostics.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Logging.Internal;

#pragma warning disable EXTEXP0013 // The one place that touches Microsoft's experimental options type, so callers never do.

/// <summary>Copies <see cref="HttpLoggingRedactionOptions"/> onto Microsoft's experimental options type.</summary>
/// <remarks>
/// Consumers configure the Headless type, so a change to Microsoft's experimental API breaks this one class instead of
/// every call site.
/// </remarks>
internal sealed class HttpLoggingRedactionOptionsSetup(IOptions<HttpLoggingRedactionOptions> source)
    : IConfigureOptions<LoggingRedactionOptions>
{
    public void Configure(LoggingRedactionOptions options)
    {
        var headless = source.Value;

        foreach (var (name, classification) in headless.RequestHeaders)
        {
            options.RequestHeadersDataClasses[name] = classification;
        }

        foreach (var (name, classification) in headless.ResponseHeaders)
        {
            options.ResponseHeadersDataClasses[name] = classification;
        }

        foreach (var (name, classification) in headless.RouteParameters)
        {
            options.RouteParameterDataClasses[name] = classification;
        }

        foreach (var prefix in headless.ExcludePathStartsWith)
        {
            options.ExcludePathStartsWith.Add(prefix);
        }

        options.IncludeUnmatchedRoutes = headless.IncludeUnmatchedRoutes;
    }
}
#pragma warning restore EXTEXP0013
