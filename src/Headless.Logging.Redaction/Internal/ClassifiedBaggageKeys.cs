// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Frozen;
using System.Diagnostics;
using System.Net;
using System.Text;
using OpenTelemetry.Context.Propagation;

namespace Headless.Logging.Internal;

/// <summary>
/// The baggage keys no outgoing call or message may carry: every property name classified with
/// <c>ClassifyProperty</c> by any host in the process.
/// </summary>
/// <remarks>
/// <para>
/// The propagators are process-wide statics, and an <c>HttpClient</c> handler copies the runtime one when it is
/// constructed, so the filter cannot be per container. The set is the union of every host's names, read when a
/// propagator injects: a library that classifies a name after the app's call still has it dropped, and a union can only
/// drop more.
/// </para>
/// <para>
/// The runtime propagator is wrapped when the first name is classified, before any host builds a handler. The
/// OpenTelemetry one is wrapped when a tracer with <c>AddHeadlessTextRedaction</c> is built, because the SDK assigns
/// its default then.
/// </para>
/// </remarks>
internal static class ClassifiedBaggageKeys
{
    private static readonly Lock _Lock = new();
    private static volatile FrozenSet<string> _keys = [];
    private static volatile bool _textMapInstalled;

    /// <summary>The current union, matched ignoring case like log property names.</summary>
    public static FrozenSet<string> Current => _keys;

    /// <summary>Whether this process installed the filter on OpenTelemetry's default propagator.</summary>
    public static bool TextMapInstalled => _textMapInstalled;

    /// <summary>Adds <paramref name="names"/> to the union, then wraps the runtime propagator if it is not yet.</summary>
    public static void Add(IEnumerable<string> names)
    {
        lock (_Lock)
        {
            var before = _keys;
            var merged = before
                .Union(names, StringComparer.OrdinalIgnoreCase)
                .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

            if (merged.Count != before.Count)
            {
                _keys = merged;
            }

            if (
                _keys.Count > 0
                && DistributedContextPropagator.Current is not BaggageFilteringDistributedContextPropagator
            )
            {
                DistributedContextPropagator.Current = new BaggageFilteringDistributedContextPropagator(
                    DistributedContextPropagator.Current,
                    static () => _keys
                );
            }
        }
    }

    /// <summary>
    /// Wraps OpenTelemetry's default propagator when any name is classified and it is not wrapped yet. Only the SDK can
    /// assign the default, so the caller passes its setter, after running the SDK's static constructor, which assigns
    /// the default too and would otherwise replace the wrapper.
    /// </summary>
    public static void InstallTextMap(Action<TextMapPropagator> assign)
    {
        lock (_Lock)
        {
            if (_keys.Count == 0 || Propagators.DefaultTextMapPropagator is BaggageFilteringTextMapPropagator)
            {
                return;
            }

            assign(new BaggageFilteringTextMapPropagator(Propagators.DefaultTextMapPropagator, static () => _keys));
            _textMapInstalled = true;
        }
    }

    /// <summary>
    /// Returns a baggage header without its classified entries, or <see langword="null"/> when none is left. An entry
    /// is <c>key=value</c> with optional <c>;properties</c>, and its key is percent-encoded.
    /// </summary>
    public static string? FilterHeader(string header, FrozenSet<string> keys)
    {
        StringBuilder? kept = null;
        var dropped = false;

        foreach (var range in header.AsSpan().Split(','))
        {
            var entry = header.AsSpan(range).Trim();

            if (entry.IsEmpty)
            {
                continue;
            }

            var equals = entry.IndexOf('=');
            var key = WebUtility.UrlDecode((equals < 0 ? entry : entry[..equals]).Trim().ToString());

            if (keys.Contains(key))
            {
                dropped = true;

                continue;
            }

            kept ??= new StringBuilder(header.Length);

            if (kept.Length > 0)
            {
                kept.Append(',');
            }

            kept.Append(entry);
        }

        if (!dropped)
        {
            return header;
        }

        return kept?.ToString();
    }
}
