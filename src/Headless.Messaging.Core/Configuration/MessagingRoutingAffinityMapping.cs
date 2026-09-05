// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Configuration;

/// <summary>Immutable native key mapping for a locally verified destination configuration.</summary>
/// <remarks>Affinity requires stable broker topology. It does not promise ordering or handler exclusivity.</remarks>
[PublicAPI]
public sealed class MessagingRoutingAffinityMapping
{
    public MessagingRoutingAffinityMapping(
        string nativeHeader,
        int? maximumKeyLength = null,
        bool printableAsciiOnly = false,
        IReadOnlyCollection<string>? matchingHeaders = null
    )
    {
        NativeHeader = Argument.IsNotNullOrWhiteSpace(nativeHeader);
        if (maximumKeyLength is { } maximum)
        {
            Argument.IsPositive(maximum);
        }

        MaximumKeyLength = maximumKeyLength;
        PrintableAsciiOnly = printableAsciiOnly;
        MatchingHeaders = (matchingHeaders ?? []).Append(nativeHeader).ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>The existing provider header adapted to the native broker key.</summary>
    public string NativeHeader { get; }

    /// <summary>Maximum key length, in UTF-16 code units, when the provider imposes one.</summary>
    public int? MaximumKeyLength { get; }

    /// <summary>Whether keys are restricted to ASCII characters from ! through ~ (without spaces).</summary>
    public bool PrintableAsciiOnly { get; }

    /// <summary>Raw provider headers that must agree with the typed key when supplied.</summary>
    public FrozenSet<string> MatchingHeaders { get; }

    /// <summary>Validates the typed key and returns it, falling back to the legacy native header for unkeyed messages.</summary>
    public string? ResolveKey(TransportMessage message)
    {
        if (message.RoutingAffinityKey is not { } key)
        {
            return message.Headers.TryGetValue(NativeHeader, out var raw) ? raw : null;
        }

        Validate(key, message.Headers);
        return key;
    }

    /// <summary>Rejects invalid keys or conflicting raw adapters before any persistence or broker effects.</summary>
    public void Validate(string key, IDictionary<string, string?> headers)
    {
        Argument.IsNotNullOrWhiteSpace(key);
        if (
            key.Any(char.IsControl)
            || (MaximumKeyLength is { } maximum && key.Length > maximum)
            || (PrintableAsciiOnly && key.Any(static character => character is < '!' or > '~'))
        )
        {
            throw new InvalidOperationException($"Routing affinity key is invalid for native header '{NativeHeader}'.");
        }

        foreach (var header in MatchingHeaders)
        {
            if (headers.TryGetValue(header, out var raw) && !string.Equals(key, raw, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Routing affinity key conflicts with provider header '{header}'.");
            }
        }
    }

    /// <summary>Rejects a typed key when the current transport topology has no native affinity mapping.</summary>
    public static void RejectUnsupported(TransportMessage message, string provider)
    {
        if (message.RoutingAffinityKey is not null)
        {
            throw new InvalidOperationException($"Routing affinity is unsupported by {provider} for '{message.Name}'.");
        }
    }
}

/// <summary>A registered logical destination and its immutable native affinity mapping.</summary>
[PublicAPI]
public sealed record MessagingRoutingAffinityRoute(
    MessageLane Lane,
    string MessageName,
    MessagingRoutingAffinityMapping Mapping
);
