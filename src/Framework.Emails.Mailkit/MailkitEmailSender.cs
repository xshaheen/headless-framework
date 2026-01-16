// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Framework.Emails.Mailkit;

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
        using var mimeMessage = await request.ConvertToMimeMessageAsync(cancellationToken).AnyContext();

        var client = pool.Get();
        try
        {
            await _EnsureConnectedAsync(client, settings, cancellationToken).AnyContext();
            await client.SendAsync(mimeMessage, cancellationToken).AnyContext();
        }
        catch (MailKit.Net.Smtp.SmtpCommandException ex)
        {
            logger.LogWarning(ex, "SMTP command failed: {StatusCode}", ex.StatusCode);
            return SendSingleEmailResponse.Failed($"SMTP error: {ex.Message}");
        }
        catch (MailKit.Net.Smtp.SmtpProtocolException ex)
        {
            logger.LogError(ex, "SMTP protocol error");
            return SendSingleEmailResponse.Failed($"Protocol error: {ex.Message}");
        }
        catch (AuthenticationException ex)
        {
            logger.LogCritical(ex, "SMTP authentication failed");
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
            .AnyContext();

        if (options.HasCredentials)
        {
            await client.AuthenticateAsync(options.User, options.Password, cancellationToken).AnyContext();
        }
    }
}
