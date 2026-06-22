// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Headless.Emails.Mailkit;

/// <summary>
/// <see cref="IEmailSender"/> implementation backed by MailKit over SMTP.
/// </summary>
/// <remarks>
/// SMTP clients are pooled via <see cref="ObjectPool{T}"/>. A client is reconnected
/// automatically if it is found disconnected when retrieved from the pool; disconnected
/// or faulted clients are discarded rather than returned. The pool size is governed by
/// <see cref="MailkitSmtpOptions.MaxPoolSize"/>.
/// <para>
/// Transient SMTP errors (<see cref="MailKit.Net.Smtp.SmtpCommandException"/>,
/// <see cref="MailKit.Net.Smtp.SmtpProtocolException"/>) are logged and surfaced as a
/// failed <see cref="SendSingleEmailResponse"/> rather than thrown.
/// Authentication failures are logged at critical level and rethrown.
/// </para>
/// </remarks>
public sealed class MailkitEmailSender(
    ObjectPool<SmtpClient> pool,
    IOptionsMonitor<MailkitSmtpOptions> options,
    string? optionsName,
    ILogger<MailkitEmailSender> logger
) : IEmailSender
{
    /// <summary>
    /// Sends a single email via SMTP using a pooled MailKit client.
    /// </summary>
    /// <param name="request">The email message to send.</param>
    /// <param name="cancellationToken">Token used to cancel the send operation.</param>
    /// <returns>
    /// A successful response when the SMTP server accepts the message; a failed response
    /// when an SMTP command or protocol error occurs.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when both <see cref="SendSingleEmailRequest.MessageText"/> and
    /// <see cref="SendSingleEmailRequest.MessageHtml"/> are <see langword="null"/>.
    /// </exception>
    /// <exception cref="System.Security.Authentication.AuthenticationException">
    /// Thrown when the SMTP server rejects the configured credentials.
    /// </exception>
    public async ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request.MessageText is null && request.MessageHtml is null)
        {
            throw new InvalidOperationException("At least one of plainMessage or htmlMessage must be provided.");
        }

        // Read the snapshot for this instance's options name (null = the default/unkeyed sender). Keyed senders
        // must not read CurrentValue, which always binds the default options.
        var settings = options.Get(optionsName);
        using var mimeMessage = await request.ConvertToMimeMessageAsync(cancellationToken).ConfigureAwait(false);

        var client = pool.Get();
        try
        {
            await _EnsureConnectedAsync(client, settings, cancellationToken).ConfigureAwait(false);
            await client.SendAsync(mimeMessage, cancellationToken).ConfigureAwait(false);
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
            logger.LogSmtpAuthenticationFailed(ex);
            throw;
        }
        finally
        {
            pool.Return(client);
        }

        return SendSingleEmailResponse.Succeeded();
    }

    private static async Task _EnsureConnectedAsync(
        SmtpClient client,
        MailkitSmtpOptions options,
        CancellationToken cancellationToken
    )
    {
        if (client.IsConnected)
        {
            return;
        }

        await client
            .ConnectAsync(
                host: options.Server,
                port: options.Port,
                options: options.SocketOptions,
                cancellationToken: cancellationToken
            )
            .ConfigureAwait(false);

        if (options.HasCredentials)
        {
            await client.AuthenticateAsync(options.User!, options.Password!, cancellationToken).ConfigureAwait(false);
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
}
