// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Diagnostics;

namespace Headless.Logging.Internal;

/// <summary>
/// Drops classified keys from the baggage the runtime propagator writes for an outgoing <c>HttpClient</c> call, and
/// passes everything else, trace context included, through the propagator it wraps.
/// </summary>
/// <remarks>
/// The runtime propagator reads baggage from the <see cref="Activity"/> chain, which cannot be filtered without
/// changing the span, so the written header is filtered instead: both the W3C <c>baggage</c> header and the legacy
/// <c>Correlation-Context</c>. A header left with no entry is not written. An activity carrying no classified key goes
/// straight to the inner propagator.
/// </remarks>
internal sealed class BaggageFilteringDistributedContextPropagator(
    DistributedContextPropagator inner,
    Func<FrozenSet<string>> keys
) : DistributedContextPropagator
{
    public DistributedContextPropagator Inner { get; } = inner;

    public override IReadOnlyCollection<string> Fields => Inner.Fields;

    public override void Inject(Activity? activity, object? carrier, PropagatorSetterCallback? setter)
    {
        var classified = keys();

        if (setter is null || activity is null || classified.Count == 0 || !_CarriesClassified(activity, classified))
        {
            Inner.Inject(activity, carrier, setter);

            return;
        }

        Inner.Inject(
            activity,
            carrier,
            (target, field, value) =>
            {
                if (
                    !string.Equals(field, "baggage", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(field, "Correlation-Context", StringComparison.OrdinalIgnoreCase)
                )
                {
                    setter(target, field, value);

                    return;
                }

                if (ClassifiedBaggageKeys.FilterHeader(value, classified) is { } filtered)
                {
                    setter(target, field, filtered);
                }
            }
        );
    }

    public override void ExtractTraceIdAndState(
        object? carrier,
        PropagatorGetterCallback? getter,
        out string? traceId,
        out string? traceState
    ) => Inner.ExtractTraceIdAndState(carrier, getter, out traceId, out traceState);

    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(
        object? carrier,
        PropagatorGetterCallback? getter
    ) => Inner.ExtractBaggage(carrier, getter);

    private static bool _CarriesClassified(Activity activity, FrozenSet<string> classified)
    {
        foreach (var (key, _) in activity.Baggage)
        {
            if (classified.Contains(key))
            {
                return true;
            }
        }

        return false;
    }
}
