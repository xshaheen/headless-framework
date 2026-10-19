// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging;

namespace Tests.Ledger;

/// <summary>
/// Wraps a host's Queue transport so the ledger sees every send the host makes, each attempt with its outcome. It can
/// also break the transport on purpose for chosen keys, which is how a negative control removes a delivery guarantee
/// and proves the ledger catches it.
/// </summary>
internal sealed class LedgerTransport(
    IQueueTransport inner,
    MessageLedger ledger,
    string host,
    TransportSabotage sabotage
) : IQueueTransport
{
    public BrokerAddress BrokerAddress => inner.BrokerAddress;

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        var key = message.Id;

        if (sabotage.Swallows(key))
        {
            // The send never reaches the broker, yet the caller is told it did: the outbox marks the row sent and no
            // retry will ever find it.
            ledger.RecordSend(key, host, succeeded: true);
            return OperateResult.Success;
        }

        OperateResult result;

        try
        {
            result = await inner.SendAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ledger.RecordSend(key, host, succeeded: false);
            throw;
        }

        ledger.RecordSend(key, host, result.Succeeded);

        if (result.Succeeded && sabotage.RedeliversUnderNewId(key))
        {
            // A fresh transport ID gets past the inbox, so the handler runs a second time for the same business key.
            var headers = new Dictionary<string, string?>(message.Headers, StringComparer.Ordinal)
            {
                [Headers.MessageId] = key + ".again",
            };

            await inner.SendAsync(new TransportMessage(headers, message.Body), cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    public ValueTask DisposeAsync() => inner.DisposeAsync();
}

/// <summary>
/// The keys whose sends a <see cref="LedgerTransport"/> breaks on purpose; empty for every real scenario. A negative
/// control names its keys once the run that makes them exists, and before it publishes any of them.
/// </summary>
public sealed class TransportSabotage
{
    private readonly ConcurrentDictionary<string, bool> _swallowed = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, bool> _redelivered = new(StringComparer.Ordinal);

    /// <summary>Reports every send of <paramref name="keys"/> as successful without sending it.</summary>
    public void SwallowSends(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            _swallowed[key] = true;
        }
    }

    /// <summary>Sends <paramref name="keys"/> a second time under a new transport message ID.</summary>
    public void RedeliverUnderNewId(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            _redelivered[key] = true;
        }
    }

    internal bool Swallows(string key) => _swallowed.ContainsKey(key);

    internal bool RedeliversUnderNewId(string key) => _redelivered.ContainsKey(key);
}
