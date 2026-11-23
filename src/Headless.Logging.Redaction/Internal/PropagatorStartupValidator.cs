// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using Headless.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Context.Propagation;

namespace Headless.Logging.Internal;

/// <summary>
/// Warns once at startup when a classified name exists but a propagator static no longer filters baggage, because code
/// assigned the static after the filter was installed.
/// </summary>
/// <remarks>
/// It reads two statics and writes nothing. OpenTelemetry's default is checked only once this process installed the
/// filter on it, which happens when a tracer is built, so the first start of a host has nothing to compare yet.
/// </remarks>
internal sealed class PropagatorStartupValidator(ILoggerFactory loggerFactory) : IHeadlessStartupValidator
{
    public Task ValidateAsync(CancellationToken cancellationToken)
    {
        if (ClassifiedBaggageKeys.Current.Count == 0)
        {
            return Task.CompletedTask;
        }

        var logger = loggerFactory.CreateLogger<PropagatorStartupValidator>();

        if (DistributedContextPropagator.Current is not BaggageFilteringDistributedContextPropagator)
        {
            logger.LogPropagatorNotFiltering(
                "DistributedContextPropagator.Current",
                DistributedContextPropagator.Current.GetType().FullName ?? "unknown"
            );
        }

        if (
            ClassifiedBaggageKeys.TextMapInstalled
            && Propagators.DefaultTextMapPropagator is not BaggageFilteringTextMapPropagator
        )
        {
            logger.LogPropagatorNotFiltering(
                "Propagators.DefaultTextMapPropagator",
                Propagators.DefaultTextMapPropagator.GetType().FullName ?? "unknown"
            );
        }

        return Task.CompletedTask;
    }
}

internal static partial class PropagatorStartupLog
{
    [LoggerMessage(
        EventId = 2,
        EventName = "BaggageFilterReplaced",
        Level = LogLevel.Warning,
        Message = "Classified baggage keys are not dropped from outgoing calls: {Static} holds {PropagatorType}, which was assigned after AddHeadlessRedaction installed the filter. Assign your propagator before calling AddHeadlessRedaction, or before building the tracer for OpenTelemetry's default."
    )]
    internal static partial void LogPropagatorNotFiltering(this ILogger logger, string @static, string propagatorType);
}
