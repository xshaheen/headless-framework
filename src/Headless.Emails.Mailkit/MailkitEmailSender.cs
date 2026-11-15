// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Headless.Emails.Mailkit;

/// <summary>
/// Sends email messages through MailKit over SMTP.
/// </summary>
/// <remarks>
/// SMTP clients are pooled through <see cref="ObjectPool{T}"/>. A pooled connection idle for longer than
/// <see cref="MailkitSmtpOptions.IdleProbeThreshold"/> is probed with <c>NOOP</c> before it is reused, and a
/// client found disconnected, or whose session the server dropped while it sat idle, is reconnected
/// automatically. A connection used more recently is trusted without the probe; if the server dropped it
/// anyway, the send reconnects and retries once, but only when the failure came before the server accepted
/// <c>MAIL FROM</c>, so the message never reached <c>DATA</c> and cannot be delivered twice. Only a client that
/// just completed a send is returned to the pool; a client whose send failed or was canceled is disposed, and
/// disconnected or connected-but-unauthenticated
/// clients are never pooled, so a pooled connection is never reused in a state that would skip
/// authentication. The pool size is governed by <see cref="MailkitSmtpOptions.MaxPoolSize"/>.
/// <para>
/// Every send failure is surfaced as a failed <see cref="SendSingleEmailResponse"/> rather than thrown, per
/// the <see cref="IEmailSender"/> contract: SMTP command and protocol errors
/// (<see cref="SmtpCommandException"/>, <see cref="SmtpProtocolException"/>),
/// authentication failures (<see cref="AuthenticationException"/>), and connect, TLS, and transport faults
/// (for example <see cref="IOException"/>, socket errors, TLS handshake failures, and connect timeouts).
/// Authentication failures are additionally logged at critical level because they signal a configuration
/// error. Only the caller's own cancellation (an <see cref="OperationCanceledException"/> raised while the
/// caller's token is canceled) and argument validation propagate; a connect-timeout cancellation is returned
/// as a failure, not thrown. On success the SMTP server's final response is surfaced as
/// <see cref="SendSingleEmailResponse.ProviderMessageId"/>, and it typically embeds the server's queue id.
/// </para>
/// </remarks>
internal sealed class MailkitEmailSender(
    ObjectPool<SmtpClient> pool,
    IOptionsMonitor<MailkitSmtpOptions> options,
    string? optionsName,
    ILogger<MailkitEmailSender> logger
) : IEmailSender
{
    /// <summary>
    /// Sends an email message over SMTP using a pooled MailKit client.
    /// </summary>
    /// <param name="request">The email message to send.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A successful response carrying the SMTP server's final response as the provider message identifier
    /// when the server accepts the message; a failed response when an SMTP command, protocol, or
    /// authentication error, or a connect, TLS, or transport fault (including a connect timeout) occurs.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Both <see cref="SendSingleEmailRequest.MessageText"/> and <see cref="SendSingleEmailRequest.MessageHtml"/>
    /// are <see langword="null"/>, empty, or contain only white space.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// The operation was canceled through <paramref name="cancellationToken"/>.
    /// </exception>
    public async ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        request.EnsureHasBody();

        // Read the snapshot for this instance's options name (null = the default/unkeyed sender). Keyed senders
        // must not read CurrentValue, which always binds the default options.
        var settings = options.Get(optionsName);
        using var mimeMessage = await request.ConvertToMimeMessageAsync(cancellationToken).ConfigureAwait(false);

        var client = pool.Get();
        var reusable = false;
        string serverResponse;
        try
        {
            var connection = await _EnsureConnectedAsync(client, settings, cancellationToken).ConfigureAwait(false);

            try
            {
                serverResponse = await _SendAsync(client, mimeMessage, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
                when (connection == ConnectionReadiness.TrustedWithoutProbe
                    && !cancellationToken.IsCancellationRequested
                    && client is PooledSmtpClient { SenderAccepted: false }
                    && _IsDroppedSession(ex)
                )
            {
                // The unprobed connection was dead. The server had not accepted MAIL FROM, so MailKit never sent
                // DATA and the server holds no part of the message: one retry on a fresh session cannot duplicate
                // it. A failure after that point is surfaced instead, because the message may already be queued.
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogPooledSmtpSessionDroppedOnSend(ex.GetType().Name);
                }

                await _ConnectAsync(client, settings, cancellationToken).ConfigureAwait(false);
                serverResponse = await _SendAsync(client, mimeMessage, cancellationToken).ConfigureAwait(false);
            }

            (client as PooledSmtpClient)?.MarkSendCompleted();
            reusable = true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Only the caller's own cancellation propagates. A timeout-CTS-induced cancellation — the connect
            // timeout fires while the caller's token is NOT cancelled — is a delivery failure and falls through
            // to the general handler below.
            throw;
        }
        catch (SmtpCommandException ex)
        {
            logger.LogSmtpCommandFailed(ex.StatusCode, ex.ErrorCode);
            return SendSingleEmailResponse.Failed($"SMTP error: {ex.Message}");
        }
        catch (SmtpProtocolException ex)
        {
            logger.LogSmtpProtocolError(ex);
            return SendSingleEmailResponse.Failed($"Protocol error: {ex.Message}");
        }
        catch (AuthenticationException ex)
        {
            // Per the IEmailSender contract, a credential rejection is returned as a failed response rather
            // than thrown; it is logged at critical level because it signals a configuration error.
            logger.LogSmtpAuthenticationFailed(ex);
            return SendSingleEmailResponse.Failed($"Authentication error: {ex.Message}");
        }
        catch (Exception ex)
        {
            // Connect/TLS/transport faults (IOException, SocketException, TLS handshake failures, and connect
            // timeouts surfaced as a non-caller cancellation) are returned as a failed response per the
            // IEmailSender return-not-throw contract. Log the exception type only — its message can carry
            // host/connection detail that does not belong in log sinks.
            logger.LogSmtpSendFailed(ex.GetType().Name);
            return SendSingleEmailResponse.FromException(ex);
        }
        finally
        {
            // A failed or canceled send can leave the session mid-transaction or the socket half-closed, so
            // only a client that just completed a send goes back to the pool; the next send connects afresh.
            if (reusable)
            {
                pool.Return(client);
            }
            else
            {
                client.Dispose();
            }
        }

        return SendSingleEmailResponse.Succeeded(string.IsNullOrWhiteSpace(serverResponse) ? null : serverResponse);
    }

    private static async Task<string> _SendAsync(
        SmtpClient client,
        MimeKit.MimeMessage message,
        CancellationToken cancellationToken
    )
    {
        (client as PooledSmtpClient)?.BeginSend();

        // MailKit returns the SMTP server's final free-form response (typically embedding the queue id).
        return await client.SendAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ConnectionReadiness> _EnsureConnectedAsync(
        SmtpClient client,
        MailkitSmtpOptions options,
        CancellationToken cancellationToken
    )
    {
        // A pooled client that is connected and (when required) authenticated is reusable only while the
        // server still holds the session. IsConnected does not observe a remote close, and relays drop idle
        // sessions within minutes, so a connection idle past the threshold is probed, and a failed NOOP probe
        // means reconnect rather than a failed send. A client that is not a PooledSmtpClient has no usage
        // record, so it is always probed.
        if (client.IsConnected && (!options.HasCredentials || client.IsAuthenticated))
        {
            if (client is PooledSmtpClient pooled && pooled.WasUsedWithin(options.IdleProbeThreshold))
            {
                return ConnectionReadiness.TrustedWithoutProbe;
            }

            if (await _IsSessionAliveAsync(client, options, cancellationToken).ConfigureAwait(false))
            {
                return ConnectionReadiness.Probed;
            }
        }

        await _ConnectAsync(client, options, cancellationToken).ConfigureAwait(false);

        return ConnectionReadiness.Fresh;
    }

    private static async Task _ConnectAsync(
        SmtpClient client,
        MailkitSmtpOptions options,
        CancellationToken cancellationToken
    )
    {
        // Bound the connect/authenticate phase by the configured timeout. SmtpClient.Timeout only
        // governs subsequent read/write operations, not the initial TCP/TLS connect.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.Timeout);

        // A stale or connected-but-unauthenticated client must be reset first, and so must a client whose send
        // got a 421, which MailKit leaves connected. Skip QUIT: on a dropped session it can only fail.
        if (client.IsConnected)
        {
            await client.DisconnectAsync(quit: false, timeoutCts.Token).ConfigureAwait(false);
        }

        await client
            .ConnectAsync(
                host: options.Server,
                port: options.Port,
                options: options.SocketOptions,
                cancellationToken: timeoutCts.Token
            )
            .ConfigureAwait(false);

        if (options.HasCredentials)
        {
            await client.AuthenticateAsync(options.User!, options.Password!, timeoutCts.Token).ConfigureAwait(false);
        }
    }

    /// <summary>How <see cref="_EnsureConnectedAsync"/> made a client ready to send.</summary>
    private enum ConnectionReadiness
    {
        /// <summary>The client connected (and authenticated) for this send.</summary>
        Fresh,

        /// <summary>The pooled session answered a <c>NOOP</c> probe.</summary>
        Probed,

        /// <summary>The pooled session was used within the idle threshold and was not probed.</summary>
        TrustedWithoutProbe,
    }

    /// <summary>
    /// Returns whether <paramref name="exception"/> shows the session was gone: a closed or reset socket, a read or
    /// write timeout on a connection that went silent, an unexpected disconnect, or a <c>421</c>
    /// service-not-available reply, which servers send when they time out an idle session.
    /// </summary>
    private static bool _IsDroppedSession(Exception exception)
    {
        return exception
            is ServiceNotConnectedException
                or SmtpProtocolException
                or IOException
                or TimeoutException
                or System.Net.Sockets.SocketException
                or SmtpCommandException { StatusCode: SmtpStatusCode.ServiceNotAvailable };
    }

    private async Task<bool> _IsSessionAliveAsync(
        SmtpClient client,
        MailkitSmtpOptions options,
        CancellationToken cancellationToken
    )
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.Timeout);

        try
        {
            await client.NoOpAsync(timeoutCts.Token).ConfigureAwait(false);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A closed socket, a 421 reply, or a probe timeout all mean the session is gone. The reconnect
            // that follows surfaces any real fault.
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogPooledSmtpSessionDropped(ex.GetType().Name);
            }

            return false;
        }
    }
}

internal static partial class MailkitEmailSenderLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "SmtpCommandFailed",
        Level = LogLevel.Warning,
        Message = "SMTP command failed: {StatusCode} ({ErrorCode})"
    )]
    // No exception argument: an SmtpCommandException's message is the server's reply, which usually names the
    // rejected recipient ("550 5.1.1 <user@example.com>: Recipient address rejected"), and a log sink must not get it.
    internal static partial void LogSmtpCommandFailed(
        this ILogger logger,
        SmtpStatusCode statusCode,
        SmtpErrorCode errorCode
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "SmtpProtocolError",
        Level = LogLevel.Error,
        Message = "SMTP protocol error"
    )]
    internal static partial void LogSmtpProtocolError(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3,
        EventName = "SmtpAuthenticationFailed",
        Level = LogLevel.Critical,
        Message = "SMTP authentication failed"
    )]
    internal static partial void LogSmtpAuthenticationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4,
        EventName = "SmtpSendFailed",
        Level = LogLevel.Error,
        Message = "Failed to send email via SMTP. ExceptionType={ExceptionType}"
    )]
    internal static partial void LogSmtpSendFailed(this ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = 5,
        EventName = "PooledSmtpSessionDropped",
        Level = LogLevel.Debug,
        Message = "Pooled SMTP session failed its NOOP probe; reconnecting. ExceptionType={ExceptionType}"
    )]
    internal static partial void LogPooledSmtpSessionDropped(this ILogger logger, string exceptionType);

    [LoggerMessage(
        EventId = 6,
        EventName = "PooledSmtpSessionDroppedOnSend",
        Level = LogLevel.Debug,
        Message = "Pooled SMTP session was gone before the server accepted the sender; reconnecting and retrying once. ExceptionType={ExceptionType}"
    )]
    internal static partial void LogPooledSmtpSessionDroppedOnSend(this ILogger logger, string exceptionType);
}
