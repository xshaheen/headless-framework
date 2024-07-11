using Framework.Emails.Contracts;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Framework.Emails.Mailkit;

internal sealed class MailkitEmailSender(IOptionsMonitor<MailkitSmtpSettings> options) : IEmailSender
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

        message.From.Add(
            new MailboxAddress(
                name: request.From.DisplayName ?? request.From.EmailAddress,
                address: request.From.EmailAddress
            )
        );

        foreach (var toAddress in request.Destination.ToAddresses)
        {
            message.To.Add(
                new MailboxAddress(
                    name: toAddress.DisplayName ?? toAddress.EmailAddress,
                    address: toAddress.EmailAddress
                )
            );
        }

        foreach (var ccAddress in request.Destination.CcAddresses)
        {
            message.Cc.Add(
                new MailboxAddress(
                    name: ccAddress.DisplayName ?? ccAddress.EmailAddress,
                    address: ccAddress.EmailAddress
                )
            );
        }

        foreach (var bccAddress in request.Destination.BccAddresses)
        {
            message.Bcc.Add(
                new MailboxAddress(
                    name: bccAddress.DisplayName ?? bccAddress.EmailAddress,
                    address: bccAddress.EmailAddress
                )
            );
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

        message.Body = emailBuilder.ToMessageBody();

        using var client = new SmtpClient();

        await client.ConnectAsync(
            host: settings.Server,
            port: settings.Port,
            options: settings.SocketOptions ?? SecureSocketOptions.Auto,
            cancellationToken: cancellationToken
        );

        if (settings.RequiresAuthentication)
        {
            await client.AuthenticateAsync(settings.User, settings.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);

        return SendSingleEmailResponse.Succeeded();
    }
}
