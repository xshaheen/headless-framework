// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Emails.Contracts;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
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

        using var message = new MimeMessage();

        message.Subject = request.Subject;

        var fromAddress = _MapToMailboxAddress(request.From);

        message.From.Add(fromAddress);

        foreach (var to in request.Destination.ToAddresses)
        {
            var toAddress = _MapToMailboxAddress(to);
            message.To.Add(toAddress);
        }

        foreach (var cc in request.Destination.CcAddresses)
        {
            var ccAddress = _MapToMailboxAddress(cc);
            message.Cc.Add(ccAddress);
        }

        foreach (var bcc in request.Destination.BccAddresses)
        {
            var bccAddress = _MapToMailboxAddress(bcc);
            message.Bcc.Add(bccAddress);
        }

        var emailBuilder = new BodyBuilder();

        if (request.MessageText is not null)
        {
            emailBuilder.TextBody = request.MessageText;
        }

        if (request.MessageHtml is not null)
        {
            emailBuilder.HtmlBody = request.MessageHtml;
        }

        foreach (var requestAttachment in request.Attachments)
        {
            var fileStream = new MemoryStream(requestAttachment.File);
            fileStream.Seek(0, SeekOrigin.Begin);
            await emailBuilder.Attachments.AddAsync(requestAttachment.Name, fileStream, cancellationToken);
        }

        message.Body = emailBuilder.ToMessageBody();

        using var client = await _BuildClientAsync(settings, cancellationToken);
        await client.SendAsync(message, cancellationToken);
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

    private static MailboxAddress _MapToMailboxAddress(EmailRequestAddress address)
    {
        return new MailboxAddress(address.DisplayName, address.EmailAddress);
    }

    #endregion
}
