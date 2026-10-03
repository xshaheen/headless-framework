// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.Messaging.Transport;

/// <summary>
/// Derives a broker subscription name from a Bus consumer identity, so every provider maps one identity to one name the
/// same way in every process.
/// </summary>
/// <remarks>
/// An identity the broker accepts as it is becomes the name unchanged. Otherwise the name keeps a readable prefix of the
/// identity, with rejected characters replaced by <c>-</c>, and ends with <c>-</c> plus a hash of the whole identity.
/// The hash is SHA-256 rather than <see cref="string.GetHashCode()"/>, which is randomized per process, and it is taken
/// from the original identity, so two identities that normalize to the same prefix still get different names.
/// </remarks>
internal static class BusNameBuilder
{
    /// <summary>Hex characters of the SHA-256 hash appended to a shortened or normalized name.</summary>
    public const int HashLength = 12;

    private const char _Replacement = '-';

    /// <summary>Returns the broker subscription name for <paramref name="identity"/> under <paramref name="rules"/>.</summary>
    /// <param name="identity">The consumer identity, or the text a provider derives a name from.</param>
    /// <param name="rules">The broker's naming rules.</param>
    /// <returns><paramref name="identity"/> when the broker accepts it; otherwise a readable prefix and a stable hash.</returns>
    public static string Build(string identity, BusNameRules rules)
    {
        Argument.IsNotNullOrWhiteSpace(identity);
        Argument.IsNotNull(rules);

        if (_IsValid(identity, rules))
        {
            return identity;
        }

        var hash = identity.ToSha256()[..HashLength];
        var readableLength = Math.Min(identity.Length, rules.MaxLength - HashLength - 1);

        var readable = string.Create(
            readableLength,
            (identity, rules),
            static (span, state) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    var c = state.identity[i];
                    span[i] = state.rules.IsAllowed(c) ? c : _Replacement;
                }
            }
        );

        readable = rules.AlphanumericBoundaries ? _TrimToAlphanumeric(readable) : readable.TrimEnd(_Replacement);

        // The hash ends the name, so a name that must end with a letter or digit does.
        return readable.Length == 0 ? hash : $"{readable}{_Replacement}{hash}";
    }

    /// <summary>
    /// Returns the broker subscription name a Bus client of <paramref name="request"/> opens: the name of its identity
    /// for a competing subscription, which every process shares, or a name derived from the identity and the requesting
    /// host's instance id for an every-instance subscription, which no other process shares.
    /// </summary>
    /// <param name="request">The consumer client request; its lane must be <see cref="MessageLane.Bus"/>.</param>
    /// <param name="rules">The broker's naming rules.</param>
    /// <returns>A name the broker accepts, stable for one identity and one instance id.</returns>
    public static string Build(ConsumerClientRequest request, BusNameRules rules)
    {
        Argument.IsNotNull(request);

        // The hash covers the instance id too, so a shortened every-instance name stays unique per process even when
        // the readable prefix only has room for the identity.
        return request.Kind is ConsumerSubscriptionKind.EveryInstance
            ? Build($"{request.SubscriptionName}.{request.InstanceId:N}", rules)
            : Build(request.SubscriptionName, rules);
    }

    private static bool _IsValid(string name, BusNameRules rules)
    {
        if (name.Length > rules.MaxLength)
        {
            return false;
        }

        if (
            rules.AlphanumericBoundaries
            && (!char.IsAsciiLetterOrDigit(name[0]) || !char.IsAsciiLetterOrDigit(name[^1]))
        )
        {
            return false;
        }

        foreach (var c in name)
        {
            if (!rules.IsAllowed(c))
            {
                return false;
            }
        }

        return true;
    }

    private static string _TrimToAlphanumeric(string value)
    {
        var start = 0;
        var end = value.Length;

        while (start < end && !char.IsAsciiLetterOrDigit(value[start]))
        {
            start++;
        }

        while (end > start && !char.IsAsciiLetterOrDigit(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }
}
