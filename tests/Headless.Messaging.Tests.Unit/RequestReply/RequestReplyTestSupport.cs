// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Text;
using System.Threading.Channels;
using Headless.Messaging;
using Headless.Messaging.Internal;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace Tests.RequestReply;

public sealed record PriceQuoteRequest(string Sku);

public sealed record PriceQuote(decimal Price);

/// <summary>
/// The contract every request/reply test host declares for <see cref="PriceQuote"/>, so a reply's contract headers are
/// compared against known literals rather than against whatever the registry resolves.
/// </summary>
internal static class PriceQuoteContract
{
    public const string Name = "tests.quote";
    public const string Version = "2";
}

/// <summary>
/// The request/reply tests share process-wide meters, so they run one class at a time to keep each class's measurements
/// its own.
/// </summary>
[CollectionDefinition(Name)]
public sealed class RequestReplyCollection
{
    public const string Name = "RequestReply";
}

/// <summary>
/// Stands in for the broker's Queue lane and the remote responder: it records every request that leaves the caller and
/// can answer it through the caller's real reply channel, the way a responder host would.
/// </summary>
internal sealed class FakeResponder : IQueueTransport
{
    private readonly Channel<TransportMessage> _requests = Channel.CreateUnbounded<TransportMessage>();

    public ConcurrentQueue<TransportMessage> Sent { get; } = new();

    /// <summary>Runs inside the transport send, before it returns, like a responder that answers instantly.</summary>
    public Func<TransportMessage, ValueTask>? OnRequest { get; set; }

    public Exception? FailWith { get; set; }

    /// <summary>
    /// Makes each send reach the broker but never be acknowledged, like a broker that stalls after accepting the
    /// request, so the send ends only when the publish token is canceled.
    /// </summary>
    public bool StallAfterSend { get; set; }

    public BrokerAddress BrokerAddress => new("FakeResponder", "localhost");

    public async Task<OperateResult> SendAsync(TransportMessage message, CancellationToken cancellationToken = default)
    {
        if (FailWith is not null)
        {
            return OperateResult.Failed(FailWith);
        }

        Sent.Enqueue(message);
        await _requests.Writer.WriteAsync(message, cancellationToken);

        if (StallAfterSend)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        if (OnRequest is not null)
        {
            await OnRequest(message);
        }

        return OperateResult.Success;
    }

    /// <summary>Waits for the next request that leaves the caller.</summary>
    public async Task<TransportMessage> NextRequestAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        return await _requests.Reader.ReadAsync(timeout.Token);
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}

/// <summary>Builds replies the way a responder host sends them, and delivers them through the real reply transport.</summary>
internal static class Replies
{
    public static async Task SendOkAsync(
        IServiceProvider provider,
        TransportMessage request,
        object? response,
        Action<IDictionary<string, string?>>? customize = null
    )
    {
        var headers = _Headers(request, "ok");
        headers[Headers.MessageName] = PriceQuoteContract.Name;
        headers[Headers.ContractVersion] = PriceQuoteContract.Version;
        customize?.Invoke(headers);

        var reply = await provider
            .GetRequiredService<IMessageSerializer>()
            .SerializeToTransportMessageAsync(new Message(headers, response));
        await _SendAsync(provider, request, reply);
    }

    public static async Task SendFaultAsync(
        IServiceProvider provider,
        TransportMessage request,
        string faultBody,
        Action<IDictionary<string, string?>>? customize = null
    )
    {
        var headers = _Headers(request, "fault");
        customize?.Invoke(headers);
        await _SendAsync(provider, request, new TransportMessage(headers, Encoding.UTF8.GetBytes(faultBody)));
    }

    private static Dictionary<string, string?> _Headers(TransportMessage request, string status)
    {
        var headers = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [Headers.MessageId] = Guid.NewGuid().ToString("D"),
            [Headers.InReplyTo] = request.Headers[Headers.RequestId],
            [Headers.ReplyStatus] = status,
        };

        // A responder stamps the request envelope's tenant on the reply.
        if (request.Headers.TryGetValue(Headers.TenantId, out var tenantId) && tenantId is not null)
        {
            headers[Headers.TenantId] = tenantId;
        }

        return headers;
    }

    private static async Task _SendAsync(IServiceProvider provider, TransportMessage request, TransportMessage reply)
    {
        await provider.GetRequiredService<IReplyTransport>().SendAsync(request.Headers[Headers.ReplyTo]!, reply);
    }
}

/// <summary>Captures the request/reply instruments: dropped-reply reasons and per-call outcomes with their tags.</summary>
internal sealed class RequestReplyMeasurements : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly Channel<string> _drops = Channel.CreateUnbounded<string>();

    public RequestReplyMeasurements()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (
                string.Equals(instrument.Meter.Name, MessagingDiagnostics.SourceName, StringComparison.Ordinal)
                && instrument.Name.StartsWith("headless.messaging.request_reply.", StringComparison.Ordinal)
            )
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>(
            (instrument, _, tags, _) => _Record(instrument.Name, tags.ToArray())
        );
        _listener.SetMeasurementEventCallback<double>(
            (instrument, _, tags, _) => _Record(instrument.Name, tags.ToArray())
        );
        _listener.Start();
    }

    public ConcurrentQueue<KeyValuePair<string, object?>[]> Outcomes { get; } = new();

    public ConcurrentQueue<KeyValuePair<string, object?>[]> Durations { get; } = new();

    /// <summary>The caller-side drop reasons: every dropped reply except one refused for its reply address.</summary>
    public ConcurrentQueue<string> Drops { get; } = new();

    /// <summary>The replies a responder refused to send because the request named an invalid reply address.</summary>
    public ConcurrentQueue<string> InvalidAddressDrops { get; } = new();

    public IEnumerable<string?> OutcomeValues =>
        Outcomes.Select(static tags =>
            tags.Single(static tag =>
                string.Equals(tag.Key, "headless.messaging.request_reply.outcome", StringComparison.Ordinal)
            ).Value as string
        );

    /// <summary>Waits until a reply is dropped for <paramref name="reason"/>.</summary>
    public async Task WaitForDropAsync(string reason, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));

        while (!string.Equals(await _drops.Reader.ReadAsync(timeout.Token), reason, StringComparison.Ordinal)) { }
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private void _Record(string instrument, KeyValuePair<string, object?>[] tags)
    {
        switch (instrument)
        {
            case "headless.messaging.request_reply.requests":
                Outcomes.Enqueue(tags);
                break;
            case "headless.messaging.request_reply.duration":
                Durations.Enqueue(tags);
                break;
            case "headless.messaging.request_reply.dropped_replies":
                var reason = (string)
                    tags.Single(static tag =>
                        string.Equals(tag.Key, "headless.messaging.request_reply.drop_reason", StringComparison.Ordinal)
                    ).Value!;

                // A refused reply address is the responder's drop, never the caller's, so it is kept apart.
                if (string.Equals(reason, "invalid_reply_address", StringComparison.Ordinal))
                {
                    InvalidAddressDrops.Enqueue(reason);
                }
                else
                {
                    Drops.Enqueue(reason);
                    _drops.Writer.TryWrite(reason);
                }

                break;
        }
    }
}
