// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Emails;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Headless.Emails.Mailkit;

public sealed class MailkitEmailSender(
    ObjectPool<SmtpClient> pool,
    IOptionsMonitor<MailkitSmtpOptions> options,
    ILogger<MailkitEmailSender> logger
) : IEmailSender
{
    public async ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request.MessageText is null && request.MessageHtml is null)
        {
            throw new InvalidOperationException("At least one of plainMessage or htmlMessage must be provided.");
        }

        var settings = options.CurrentValue;
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
            await client.AuthenticateAsync(options.User, options.Password, cancellationToken).ConfigureAwait(false);
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
