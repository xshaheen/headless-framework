// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
/// SMTP clients are pooled through <see cref="ObjectPool{T}"/>. Delivery and transport errors
/// return a failed <see cref="SendSingleEmailResponse"/> rather than throwing exceptions.
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
    /// <returns>A task that represents the asynchronous operation, containing the send outcome response.</returns>
    /// <exception cref="InvalidOperationException">Both <see cref="SendSingleEmailRequest.MessageText"/> and <see cref="SendSingleEmailRequest.MessageHtml"/> are empty or contain only white space.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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
        string serverResponse;
        try
        {
            await _EnsureConnectedAsync(client, settings, cancellationToken).ConfigureAwait(false);

            // MailKit returns the SMTP server's final free-form response (typically embedding the queue id).
            serverResponse = await client.SendAsync(mimeMessage, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Only the caller's own cancellation propagates. A timeout-CTS-induced cancellation — the connect
            // timeout fires while the caller's token is NOT cancelled — is a delivery failure and falls through
            // to the general handler below.
            throw;
        }
        catch (MailKit.Net.Smtp.SmtpCommandException ex)
        {
            logger.LogSmtpCommandFailed(ex, ex.StatusCode);
            return SendSingleEmailResponse.Failed($"SMTP error: {ex.Message}");
        }
        catch (MailKit.Net.Smtp.SmtpProtocolException ex)
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
            pool.Return(client);
        }

        return SendSingleEmailResponse.Succeeded(string.IsNullOrWhiteSpace(serverResponse) ? null : serverResponse);
    }

    private static async Task _EnsureConnectedAsync(
        SmtpClient client,
        MailkitSmtpOptions options,
        CancellationToken cancellationToken
    )
    {
        // A pooled client that is already connected and (when required) authenticated is reusable.
        if (client.IsConnected && (!options.HasCredentials || client.IsAuthenticated))
        {
            return;
        }

        // Bound the connect/authenticate phase by the configured timeout. SmtpClient.Timeout only
        // governs subsequent read/write operations, not the initial TCP/TLS connect.
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(options.Timeout);

        // A connected-but-unauthenticated client (credentials required) must be reset before re-auth.
        if (client.IsConnected)
        {
            await client.DisconnectAsync(quit: true, timeoutCts.Token).ConfigureAwait(false);
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
}

internal static partial class MailkitEmailSenderLoggerExtensions
{
    [LoggerMessage(
        EventId = 1,
        EventName = "SmtpCommandFailed",
        Level = LogLevel.Warning,
        Message = "SMTP command failed: {StatusCode}"
    )]
    public static partial void LogSmtpCommandFailed(
        this ILogger logger,
        Exception exception,
        MailKit.Net.Smtp.SmtpStatusCode statusCode
    );

    [LoggerMessage(
        EventId = 2,
        EventName = "SmtpProtocolError",
        Level = LogLevel.Error,
        Message = "SMTP protocol error"
    )]
    public static partial void LogSmtpProtocolError(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 3,
        EventName = "SmtpAuthenticationFailed",
        Level = LogLevel.Critical,
        Message = "SMTP authentication failed"
    )]
    public static partial void LogSmtpAuthenticationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4,
        EventName = "SmtpSendFailed",
        Level = LogLevel.Error,
        Message = "Failed to send email via SMTP. ExceptionType={ExceptionType}"
    )]
    public static partial void LogSmtpSendFailed(this ILogger logger, string exceptionType);
}
