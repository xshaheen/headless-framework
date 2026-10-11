// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Cryptography;
using Headless.Messaging.Internal;

namespace Headless.Messaging.Transport;

/// <summary>
/// The one check every consumer client runs on a delivery before it hands the delivery to the core: it completes the
/// envelope of a message whose contract accepts raw payloads, then requires the identity headers every later stage
/// keys on.
/// </summary>
/// <remarks>
/// <para>
/// A non-.NET producer that publishes with its broker's own client sends none of the Headless envelope headers. For a
/// contract declared with <c>RawPayload()</c>, the address the delivery arrived on names the contract, and the gate
/// fills only what is missing: the name, an identifier derived from broker metadata, the contract version, the send
/// time, and the correlation identifier. The body is never touched.
/// </para>
/// <para>
/// On a raw route the address is the routing authority: a <c>headless-msg-name</c> header that names a different
/// contract makes the delivery malformed, so a foreign header cannot steer a message to a contract its producer did not
/// publish against. Every other route keeps header routing.
/// </para>
/// <para>
/// A malformed delivery surfaces as <see cref="InvalidDataException"/>, which every consumer client already settles
/// terminally without a handler callback.
/// </para>
/// </remarks>
internal sealed class TransportEnvelopeGate
{
    /// <summary>A gate with no raw routes, for consumer clients built without a container.</summary>
    public static TransportEnvelopeGate Strict { get; } = new([]);

    // A malformed-envelope reason can reach logs, and the name in it comes from the producer.
    private const int _MaxLoggedNameLength = 255;

    private readonly Dictionary<(MessageLane Lane, string Name), RawRoute> _rawRoutes;

    public TransportEnvelopeGate(IMessageMetadataRegistry metadataRegistry)
        : this(metadataRegistry.GetAll()) { }

    internal TransportEnvelopeGate(IEnumerable<MessageMetadata> routes)
    {
        _rawRoutes = [];

        foreach (var route in routes)
        {
            if (route.RawPayload)
            {
                _rawRoutes.TryAdd(
                    (route.Route.Lane, route.Route.MessageName),
                    new RawRoute(route.Route.MessageName, route.ContractVersion)
                );
            }
        }
    }

    /// <summary>Whether any route on <paramref name="lane"/> accepts raw payloads.</summary>
    public bool HasRawRoutes(MessageLane lane) => _rawRoutes.Keys.Any(key => key.Lane == lane);

    /// <summary>Whether the route of <paramref name="messageName"/> on <paramref name="lane"/> accepts raw payloads.</summary>
    public bool IsRaw(MessageLane lane, string? messageName) =>
        messageName is not null && _rawRoutes.ContainsKey((lane, messageName));

    /// <summary>Completes and checks a delivery that needs no broker metadata to be completed.</summary>
    /// <exception cref="InvalidDataException">The delivery is malformed.</exception>
    public void Admit(IDictionary<string, string?> headers, in EnvelopeArrival arrival)
    {
        Admit(headers, arrival, state: 0, static _ => default);
    }

    /// <summary>
    /// Completes <paramref name="headers"/> when the delivery belongs to a raw route, then requires the message
    /// identifier and name.
    /// </summary>
    /// <param name="headers">The delivery's headers, after any custom headers builder ran; completed in place.</param>
    /// <param name="arrival">Where the delivery arrived.</param>
    /// <param name="state">State for <paramref name="facts"/>, so a provider passes its delivery without a closure.</param>
    /// <param name="facts">
    /// Reads the broker metadata that completes a raw envelope. It runs at most once, and only when a header is missing.
    /// </param>
    /// <exception cref="InvalidDataException">The delivery is malformed.</exception>
    public void Admit<TState>(
        IDictionary<string, string?> headers,
        in EnvelopeArrival arrival,
        TState state,
        Func<TState, NativeEnvelopeFacts> facts
    )
    {
        var headerName = _NonBlank(headers, Headers.MessageName);
        RawRoute? raw;

        if (
            arrival.ArrivalName is { } arrivalName
            && (arrival.AddressIsAuthoritative || IsRaw(arrival.Lane, arrivalName))
        )
        {
            if (headerName is not null && !string.Equals(headerName, arrivalName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The {arrival.Transport} delivery names message '{LogSanitizer.Sanitize(headerName, _MaxLoggedNameLength)}' but arrived on the address of '{arrivalName}'."
                );
            }

            raw = _rawRoutes.TryGetValue((arrival.Lane, arrivalName), out var arrivalRoute) ? arrivalRoute : null;
        }
        else
        {
            // A header picks a raw contract only where the address names no message at all, such as a queue several
            // messages share. Where the address names a message that is not raw, a header naming a raw one must not
            // unlock completion: that would let a foreign header steer a delivery onto a contract it was not sent to.
            raw =
                arrival.ArrivalName is null
                && headerName is not null
                && _rawRoutes.TryGetValue((arrival.Lane, headerName), out var headerRoute)
                    ? headerRoute
                    : null;
        }

        if (raw is not null)
        {
            _Complete(headers, raw, arrival.Transport, state, facts);
        }

        _ApplyAddressTenant(headers, arrival);

        var messageId = _NonBlank(headers, Headers.MessageId);
        if (messageId is null || _NonBlank(headers, Headers.MessageName) is null)
        {
            throw new InvalidDataException(
                $"The {arrival.Transport} transport envelope is missing a required Messaging header."
            );
        }

        if (messageId.Length > MessageOptions.MessageIdMaxLength)
        {
            throw new InvalidDataException(
                $"The {arrival.Transport} transport envelope carries a message id longer than {MessageOptions.MessageIdMaxLength} characters."
            );
        }
    }

    private static void _Complete<TState>(
        IDictionary<string, string?> headers,
        RawRoute route,
        string transport,
        TState state,
        Func<TState, NativeEnvelopeFacts> readFacts
    )
    {
        NativeEnvelopeFacts? facts = null;

        if (_NonBlank(headers, Headers.MessageName) is null)
        {
            headers[Headers.MessageName] = route.Name;
        }

        var messageId = _NonBlank(headers, Headers.MessageId);
        if (messageId is null)
        {
            facts ??= readFacts(state);
            messageId =
                _NonBlankValue(facts.Value.ProducerMessageId)
                ?? _NonBlankValue(facts.Value.PositionId)
                ?? throw new InvalidDataException(
                    $"The {transport} delivery of raw message '{route.Name}' has no message id, and the broker gives it no stable identity."
                );
            headers[Headers.MessageId] = messageId;
        }

        if (_NonBlank(headers, Headers.ContractVersion) is null)
        {
            headers[Headers.ContractVersion] = route.Version;
        }

        if (_NonBlank(headers, Headers.SentTime) is null)
        {
            facts ??= readFacts(state);
            if (facts.Value.StoredAt is { } storedAt)
            {
                headers[Headers.SentTime] = storedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            }
        }

        // The publish path correlates a message to itself when nothing else is given, so a raw message starts its own
        // conversation the same way and a follow-up publish from its handler links back to it.
        if (_NonBlank(headers, Headers.CorrelationId) is null)
        {
            headers[Headers.CorrelationId] = messageId;
        }
    }

    private static void _ApplyAddressTenant(IDictionary<string, string?> headers, in EnvelopeArrival arrival)
    {
        if (arrival.AddressTenantError is { } error)
        {
            throw new InvalidDataException(
                $"The {arrival.Transport} delivery address carries no valid tenant: {error}"
            );
        }

        if (arrival.AddressTenant is not { } addressTenant)
        {
            return;
        }

        var headerTenant = _NonBlank(headers, Headers.TenantId);
        if (headerTenant is not null && !string.Equals(headerTenant, addressTenant, StringComparison.Ordinal))
        {
            // Picking either source would let a producer permitted on one tenant's address claim another tenant.
            throw new InvalidDataException(
                $"The {arrival.Transport} delivery's tenant header differs from the tenant in its address."
            );
        }

        headers[Headers.TenantId] = addressTenant;
    }

    private static string? _NonBlank(IDictionary<string, string?> headers, string name) =>
        headers.TryGetValue(name, out var value) ? _NonBlankValue(value) : null;

    private static string? _NonBlankValue(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record RawRoute(string Name, string Version);
}

/// <summary>Where a delivery arrived, as its consumer client knows it.</summary>
/// <param name="Transport">The transport's display name, used in malformed-envelope reasons.</param>
/// <param name="Lane">The lane of the subscription the delivery arrived on.</param>
/// <param name="ArrivalName">
/// The message name the delivery's address belongs to, from a lookup the client built when it subscribed; or
/// <see langword="null"/> when the address does not name one message.
/// </param>
internal readonly record struct EnvelopeArrival(string Transport, MessageLane Lane, string? ArrivalName)
{
    /// <summary>
    /// Whether the address names the contract even when the route is not raw, as an external subscription does.
    /// </summary>
    public bool AddressIsAuthoritative { get; init; }

    /// <summary>The tenant the address carries, such as a subject token.</summary>
    public string? AddressTenant { get; init; }

    /// <summary>Why the address was expected to carry a tenant and does not.</summary>
    public string? AddressTenantError { get; init; }
}

/// <summary>Broker metadata that completes a raw envelope.</summary>
/// <param name="ProducerMessageId">
/// The producer's own idempotency key in the broker's native field, such as <c>Nats-Msg-Id</c> or an AMQP
/// <c>message-id</c>.
/// </param>
/// <param name="PositionId">
/// An identifier built from the broker's position for the stored message: identical on every redelivery, and different
/// after the stream, topic, or entity is recreated. See <see cref="Position"/>.
/// </param>
/// <param name="StoredAt">When the broker stored the message.</param>
internal readonly record struct NativeEnvelopeFacts(
    string? ProducerMessageId,
    string? PositionId,
    DateTimeOffset? StoredAt
)
{
    /// <summary>
    /// Joins <paramref name="parts"/> into a position identifier prefixed with <paramref name="transport"/>. An identifier
    /// longer than the message id limit, which long topic or stream names can produce, is replaced by a SHA-256 of itself,
    /// so it stays deterministic.
    /// </summary>
    public static string Position(string transport, params ReadOnlySpan<string> parts)
    {
        var position = transport + ":" + string.Join(':', parts);

        if (position.Length <= MessageOptions.MessageIdMaxLength)
        {
            return position;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(position));
        return $"{transport}:sha256:{Convert.ToHexStringLower(hash)}";
    }
}
