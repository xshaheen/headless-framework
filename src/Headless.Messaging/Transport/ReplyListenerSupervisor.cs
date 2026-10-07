// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.Transport;

/// <summary>
/// Keeps a broker-backed <see cref="IReplyListener"/>'s channel open for the listener's whole life: it runs the
/// provider's serve-once pass, withdraws the address when the pass ends, waits a backoff, and runs the pass again until
/// the listener closes.
/// </summary>
/// <remarks>
/// <para>
/// A pass opens the channel, calls <see cref="Ready(string)"/> once replies can reach it, pumps replies until the channel
/// ends, and returns why it ended. A pass that throws failed; one that returns was lost. Either way the supervisor
/// withdraws the address and backs off before the next pass, so a broker that keeps accepting the channel and then
/// dropping it cannot drive a tight reopen loop. The pass releases its own broker objects before it returns or throws.
/// </para>
/// <para>
/// The provider chooses the address each pass hands out: one address for the listener's whole life when nothing else
/// can hold it, or a new one per pass when the broker may still hold the old one. Work that must start only once the
/// address is out, such as a best-effort configuration check, goes in the pass right after <see cref="Ready(string)"/>.
/// </para>
/// </remarks>
internal sealed class ReplyListenerSupervisor : IAsyncDisposable
{
    private readonly string _transport;
    private readonly IReplyListener _listener;
    private readonly Func<CancellationToken, Task<string>> _serveOnce;
    private readonly ReplyListenerBackoff _backoff;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _closing = new();
    private Task? _loop;
    private string? _readyAddress;
    private int _closed;

    /// <summary>Creates a supervisor that runs no pass until <see cref="Start"/>.</summary>
    /// <param name="transport">The broker's display name, as the shared log events name it.</param>
    /// <param name="listener">The listener this supervises, which a call on the closed listener names.</param>
    /// <param name="serveOnce">
    /// One pass of the channel, given the listener's closing token: opens it, calls <see cref="Ready(string)"/>, pumps
    /// replies until the channel ends, and returns why it ended.
    /// </param>
    /// <param name="logger">The provider's logger, which the shared events are written to.</param>
    /// <param name="backoffClock">
    /// The clock the backoff runs on; <see cref="TimeProvider.System"/> when omitted. The providers omit it: the backoff
    /// waits for a broker to come back in real time, and on a faked app clock that a test host never advances, a lost
    /// channel would never reopen. Tests of the supervisor itself pass a fake to step through the delays.
    /// </param>
    /// <param name="jitter">The backoff's jitter source; <see cref="Random.Shared"/> when omitted.</param>
    public ReplyListenerSupervisor(
        string transport,
        IReplyListener listener,
        Func<CancellationToken, Task<string>> serveOnce,
        ILogger logger,
        TimeProvider? backoffClock = null,
        Random? jitter = null
    )
    {
        _transport = transport;
        _listener = listener;
        _serveOnce = serveOnce;
        _backoff = new ReplyListenerBackoff(backoffClock ?? TimeProvider.System, jitter);
        _logger = logger;

        // Cached, so a connection event that fires after disposal still reads a cancelled token instead of throwing.
        ClosingToken = _closing.Token;
    }

    /// <summary>
    /// The address callers wait for. A provider whose client re-establishes the channel on its own withdraws and
    /// re-publishes it from its connection events.
    /// </summary>
    public ReplyAddressGate Address { get; } = new();

    /// <summary>Cancelled once the listener closes.</summary>
    public CancellationToken ClosingToken { get; }

    /// <summary>Whether the listener has started closing.</summary>
    public bool IsClosed => Volatile.Read(ref _closed) != 0;

    /// <summary>Starts the passes in the background, so an unreachable broker delays calls instead of failing the host.</summary>
    public void Start()
    {
        _loop = Task.Run(_RunAsync);
    }

    /// <summary>Waits for the current address; throws <see cref="ObjectDisposedException"/> once the listener closed.</summary>
    public ValueTask<string> WaitForAddressAsync(CancellationToken cancellationToken)
    {
        Ensure.NotDisposed(IsClosed, _listener);

        return Address.WaitAsync(cancellationToken);
    }

    /// <summary>Hands <paramref name="address"/> out: the current pass can receive replies.</summary>
    public void Ready(string address)
    {
        Address.Publish(address);
        _OnReady(address);
    }

    /// <summary>
    /// Hands <paramref name="address"/> out only while <paramref name="isLive"/> holds under the gate's lock, the lock
    /// a retraction from a connection event takes too.
    /// </summary>
    public void Ready(string address, Func<bool> isLive)
    {
        Address.Publish(address, isLive);
        _OnReady(address);
    }

    /// <summary>
    /// Fails a caller still waiting for an address, cancels the closing token, and waits for the current pass to release
    /// its broker objects.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0)
        {
            // A concurrent close still returns only once the passes stopped.
            await (_loop ?? Task.CompletedTask).ConfigureAwait(false);
            return;
        }

        Address.FailOnDispose(_listener.GetType().Name);

        await _closing.CancelAsync().ConfigureAwait(false);

        await (_loop ?? Task.CompletedTask).ConfigureAwait(false);
        _closing.Dispose();
    }

    private void _OnReady(string address)
    {
        _readyAddress = address;
        _backoff.Reset();
        _logger.ReplyListenerReady(_transport, address);
    }

    private async Task _RunAsync()
    {
        while (!ClosingToken.IsCancellationRequested)
        {
            _readyAddress = null;

            try
            {
                var reason = await _serveOnce(ClosingToken).ConfigureAwait(false);

                if (ClosingToken.IsCancellationRequested)
                {
                    return;
                }

                // Makes callers wait for the next pass instead of sending a request whose reply reaches nobody.
                Address.Retract();
                _logger.ReplyListenerLost(_transport, _readyAddress, reason, _backoff.Delay);
            }
            catch (OperationCanceledException) when (ClosingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                Address.Retract();
                _logger.ReplyListenerFailed(e, _transport, _backoff.Delay);
            }

            // A lost channel backs off as a failed one does; a pass that reached Ready reset the delay.
            if (!await _backoff.WaitAsync(ClosingToken).ConfigureAwait(false))
            {
                return;
            }
        }
    }
}

internal static partial class ReplyListenerSupervisorLog
{
    [LoggerMessage(
        EventId = 4117,
        EventName = "ReplyListenerReady",
        Level = LogLevel.Debug,
        Message = "{Transport} reply listener is receiving on reply address '{ReplyAddress}'."
    )]
    public static partial void ReplyListenerReady(this ILogger logger, string transport, string replyAddress);

    [LoggerMessage(
        EventId = 4118,
        EventName = "ReplyListenerLost",
        Level = LogLevel.Warning,
        Message = "{Transport} reply channel '{ReplyAddress}' ended ({Reason}); the listener opens it again in {RetryDelay}, and a reply published meanwhile is lost."
    )]
    public static partial void ReplyListenerLost(
        this ILogger logger,
        string transport,
        string? replyAddress,
        string reason,
        TimeSpan retryDelay
    );

    [LoggerMessage(
        EventId = 4119,
        EventName = "ReplyListenerFailed",
        Level = LogLevel.Error,
        Message = "{Transport} reply listener failed to open or serve its reply channel; retrying in {RetryDelay}."
    )]
    public static partial void ReplyListenerFailed(
        this ILogger logger,
        Exception exception,
        string transport,
        TimeSpan retryDelay
    );
}
