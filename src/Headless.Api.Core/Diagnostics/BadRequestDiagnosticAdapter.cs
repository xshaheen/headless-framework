// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging;

namespace Headless.Api.Diagnostics;

/// <summary>
/// Diagnostic observer that subscribes to the Kestrel bad-request event and writes a structured
/// warning log entry. Register with <c>DiagnosticListener.Subscribe(observer, IsEnabled)</c> on the
/// process-wide <see cref="System.Diagnostics.DiagnosticListener"/>.
/// </summary>
/// <seealso href="https://learn.microsoft.com/en-us/aspnet/core/fundamentals/servers/kestrel/diagnostics"/>
internal sealed partial class BadRequestDiagnosticAdapter(ILogger logger) : IObserver<KeyValuePair<string, object?>>
{
    /// <summary>Whether this observer handles the named diagnostic event.</summary>
    public static bool IsEnabled(string eventName)
    {
        return string.Equals(eventName, DiagnosticSources.KestrelOnBadRequest, StringComparison.Ordinal);
    }

    /// <summary>Handles the <see cref="DiagnosticSources.KestrelOnBadRequest"/> diagnostic event.</summary>
    /// <param name="value">The event name and its payload; Kestrel passes the connection's <see cref="IFeatureCollection"/>.</param>
    public void OnNext(KeyValuePair<string, object?> value)
    {
        if (!IsEnabled(value.Key) || value.Value is not IFeatureCollection featureCollection)
        {
            return;
        }

        var badRequestFeature = featureCollection.Get<IBadRequestExceptionFeature>();

        if (badRequestFeature is not null)
        {
            BadRequestEvent(logger, badRequestFeature.Error);
        }
    }

    public void OnError(Exception error) { }

    public void OnCompleted() { }

    [LoggerMessage(
        EventId = 5104,
        EventName = "BadRequestEvent",
        Level = LogLevel.Warning,
        SkipEnabledCheck = true,
        Message = "Bad request received"
    )]
    public static partial void BadRequestEvent(ILogger logger, Exception? exception);
}
