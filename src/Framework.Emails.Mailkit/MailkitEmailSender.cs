// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Framework.Emails.Mailkit;

public sealed class MailkitEmailSender(IOptionsMonitor<MailkitSmtpOptions> options, ILogger<MailkitEmailSender> logger)
    : IEmailSender
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
        using var client = await _BuildClientAsync(settings, cancellationToken).AnyContext();

        try
        {
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
            throw; // Config error - should not be swallowed
        }
        finally
        {
            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(quit: true, cancellationToken).AnyContext();
                }
                catch
                {
                    // Ignore disconnect errors during cleanup
                }
            }
        }

        return SendSingleEmailResponse.Succeeded();
    }

    private static async Task<SmtpClient> _BuildClientAsync(
        MailkitSmtpOptions options,
        CancellationToken cancellationToken
    )
    {
        var client = new SmtpClient();
        client.Timeout = (int)options.Timeout.TotalMilliseconds;

        try
        {
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

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }
}
