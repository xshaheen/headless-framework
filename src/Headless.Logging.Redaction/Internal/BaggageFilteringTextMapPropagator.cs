// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Headless.Logging.Internal;

/// <summary>
/// Hands the propagator it wraps a copy of the context without classified baggage keys, so no outgoing call or message
/// injected through OpenTelemetry carries them.
/// </summary>
/// <remarks>
/// Filtering the context rather than the written header keeps every field a custom propagator writes. A propagator that
/// reads <see cref="Baggage.Current"/> instead of the context it is given is not filtered. Extraction is unchanged.
/// </remarks>
internal sealed class BaggageFilteringTextMapPropagator(TextMapPropagator inner, Func<FrozenSet<string>> keys)
    : TextMapPropagator
{
    // BaggagePropagator.BaggageHeaderName, which OpenTelemetry keeps internal.
    private const string _BaggageHeaderName = "baggage";

    public TextMapPropagator Inner { get; } = inner;

    public override ISet<string>? Fields => Inner.Fields;

    public override void Inject<T>(PropagationContext context, T carrier, Action<T, string, string> setter)
    {
        var classified = keys();

        if (classified.Count == 0 || context.Baggage.Count == 0)
        {
            Inner.Inject(context, carrier, setter);

            return;
        }

        Dictionary<string, string>? kept = null;
        var dropped = false;

        foreach (var (key, value) in context.Baggage.GetBaggage())
        {
            if (classified.Contains(key))
            {
                dropped = true;

                continue;
            }

            (kept ??= [with(StringComparer.Ordinal)])[key] = value;
        }

        Inner.Inject(
            dropped ? new PropagationContext(context.ActivityContext, Baggage.Create(kept)) : context,
            carrier,
            setter
        );

        // BaggagePropagator writes nothing for empty baggage, so a carrier the baggage was first extracted from (such as
        // forwarded message headers) would still hold the classified entries. Overwrite the field it would have written.
        if (dropped && kept is null)
        {
            setter(carrier, _BaggageHeaderName, string.Empty);
        }
    }

    public override PropagationContext Extract<T>(
        PropagationContext context,
        T carrier,
        Func<T, string, IEnumerable<string>?> getter
    ) => Inner.Extract(context, carrier, getter);
}
