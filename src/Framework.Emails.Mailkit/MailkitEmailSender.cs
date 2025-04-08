// Copyright (c) Mahmoud Shaheen. All rights reserved.

using MailKit.Security;
using Microsoft.Extensions.Options;
using SmtpClient = MailKit.Net.Smtp.SmtpClient;

namespace Framework.Emails.Mailkit;

public sealed class MailkitEmailSender(IOptionsMonitor<MailkitSmtpOptions> options) : IEmailSender
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

        using var mimeMessage = await request.ConvertToMimeMessageAsync(cancellationToken);
        using var client = await _BuildClientAsync(settings, cancellationToken);
        await client.SendAsync(mimeMessage, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        return SendSingleEmailResponse.Succeeded();
    }

    #region Helpers

    private static async Task<SmtpClient> _BuildClientAsync(
        MailkitSmtpOptions options,
        CancellationToken cancellationToken
    )
    {
        var client = new SmtpClient();

        try
        {
            await _ConfigureClient(client, options, cancellationToken);

            return client;
        }
        catch
        {
            client.Dispose();

            throw;
        }
    }

    private static async Task _ConfigureClient(
        SmtpClient client,
        MailkitSmtpOptions options,
        CancellationToken cancellationToken
    )
    {
        await client.ConnectAsync(
            host: options.Server,
            port: options.Port,
            options: options.SocketOptions ?? SecureSocketOptions.Auto,
            cancellationToken: cancellationToken
        );

        if (options.RequiresAuthentication)
        {
            await client.AuthenticateAsync(options.User, options.Password, cancellationToken);
        }
    }

    #endregion
}
