// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Headless.Messaging.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace Tests.Forwarding;

/// <summary>Routes every forwarded call by its host name to the node registered under it, and records each call.</summary>
internal sealed class PeerNetwork
{
    private readonly ConcurrentDictionary<string, TestServer> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<SentCall> _calls = new();

    private readonly ConcurrentDictionary<string, Func<HttpRequestMessage, HttpResponseMessage?>> _answers = new(
        StringComparer.OrdinalIgnoreCase
    );

    private readonly ConcurrentQueue<(Func<SentCall, bool> Match, TaskCompletionSource<SentCall> Signal)> _waiters =
        new();

    public IReadOnlyList<SentCall> Calls => [.. _calls];

    /// <summary>Makes <paramref name="app"/> answer calls to <c>https://{host}</c> and <c>http://{host}</c>.</summary>
    public void Add(string host, WebApplication app) => _nodes[host] = app.GetTestServer();

    /// <summary>Makes calls to <paramref name="host"/> fail as a connection failure would.</summary>
    public void Remove(string host) => _nodes.TryRemove(host, out _);

    /// <summary>
    /// Answers calls to <paramref name="host"/> with what <paramref name="answer"/> returns before the node sees them;
    /// a <see langword="null"/> answer lets the call through. Pass <see langword="null"/> to stop.
    /// </summary>
    public void Answer(string host, Func<HttpRequestMessage, HttpResponseMessage?>? answer)
    {
        if (answer is null)
        {
            _answers.TryRemove(host, out _);
        }
        else
        {
            _answers[host] = answer;
        }
    }

    /// <summary>Completes with the next call that <paramref name="match"/> accepts, sent after this call.</summary>
    public Task<SentCall> NextCallAsync(Func<SentCall, bool> match)
    {
        var signal = new TaskCompletionSource<SentCall>(TaskCreationOptions.RunContinuationsAsynchronously);
        _waiters.Enqueue((match, signal));

        return signal.Task;
    }

    /// <summary>A handler that records its calls as sent by <paramref name="caller"/>.</summary>
    public HttpMessageHandler ForCaller(string caller) => new Handler(this, caller);

    public sealed record SentCall(
        string Caller,
        Uri Uri,
        HttpMethod Method,
        bool CarriedKey,
        string? Access,
        string? Actor
    );

    private sealed class Handler(PeerNetwork network, string caller) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var call = new SentCall(
                caller,
                request.RequestUri!,
                request.Method,
                request.Headers.Contains(ForwardingProtocol.KeyHeader),
                _Header(request, ForwardingProtocol.AccessHeader),
                _Header(request, ForwardingProtocol.ActorHeader)
            );
            network._calls.Enqueue(call);

            foreach (var (match, signal) in network._waiters)
            {
                if (!signal.Task.IsCompleted && match(call))
                {
                    signal.TrySetResult(call);
                }
            }

            if (
                network._answers.TryGetValue(request.RequestUri!.Host, out var answer)
                && answer(request) is { } answered
            )
            {
                return answered;
            }

            if (!network._nodes.TryGetValue(request.RequestUri!.Host, out var server))
            {
                throw new HttpRequestException("No node listens at " + request.RequestUri.Host + ".");
            }

            using var invoker = new HttpMessageInvoker(server.CreateHandler(), disposeHandler: true);

            return await invoker.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        private static string? _Header(HttpRequestMessage request, string name) =>
            request.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
    }
}
