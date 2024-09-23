// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Framework.Emails.Contracts;
using Framework.Emails.Helpers;
using Microsoft.Extensions.Logging;

namespace Framework.Emails.Aws;

/*
 * API Docs
 * https://docs.aws.amazon.com/ses/latest/APIReference/Welcome.html
 */
public sealed class AwsSesEmailSender : IEmailSender
{
    private readonly IAmazonSimpleEmailServiceV2 _client;
    private readonly ILogger<AwsSesEmailSender> _logger;

    public AwsSesEmailSender(IAmazonSimpleEmailServiceV2 client, ILogger<AwsSesEmailSender> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async ValueTask<SendSingleEmailResponse> SendAsync(
        SendSingleEmailRequest request,
        CancellationToken cancellationToken = default
    )
    {
        if (request.Attachments.Count == 0)
        {
            var simpleRequest = _CreateSimpleEmail(request);

            return await _SendAsync(simpleRequest, cancellationToken);
        }

        using var mimeMessage = await request.ConvertToMimeMessageAsync(cancellationToken);
        await using var memoryStream = new MemoryStream();
        await mimeMessage.WriteToAsync(memoryStream, cancellationToken);

        var rawRequest = new SendEmailRequest
        {
            Content = new EmailContent { Raw = new RawMessage { Data = memoryStream } },
        };

        return await _SendAsync(rawRequest, cancellationToken);
    }

    private static SendEmailRequest _CreateSimpleEmail(SendSingleEmailRequest request)
    {
        const string charset = "UTF-8";

        return new SendEmailRequest
        {
            FromEmailAddress = request.From.EmailAddress,
            Destination = new Destination
            {
                ToAddresses = request.Destination.ToAddresses.Select(x => x.EmailAddress).ToList(),
                CcAddresses = request.Destination.CcAddresses.Select(x => x.EmailAddress).ToList(),
                BccAddresses = request.Destination.BccAddresses.Select(x => x.EmailAddress).ToList(),
            },
            Content = new EmailContent
            {
                Simple = new Message
                {
                    Subject = new Content { Charset = charset, Data = request.Subject },
                    Body = new Body
                    {
                        Html = string.IsNullOrWhiteSpace(request.MessageHtml)
                            ? default
                            : new Content { Charset = charset, Data = request.MessageHtml },
                        Text = string.IsNullOrWhiteSpace(request.MessageText)
                            ? default
                            : new Content { Charset = charset, Data = request.MessageText },
                    },
                },
            },
        };
    }

    private async Task<SendSingleEmailResponse> _SendAsync(
        SendEmailRequest request,
        CancellationToken cancellationToken
    )
    {
        SendEmailResponse response;

        try
        {
            response = await _client.SendEmailAsync(request, cancellationToken);
        }
        catch (Exception ex)
            when (ex
                    is MessageRejectedException
                        or BadRequestException
                        or NotFoundException
                        or AccountSuspendedException
                        or MailFromDomainNotVerifiedException
                        or LimitExceededException
                        or TooManyRequestsException
                        or SendingPausedException
            )
        {
            throw;
        }

        if (response.HttpStatusCode.IsSuccessStatusCode())
        {
            return SendSingleEmailResponse.Succeeded();
        }

        _logger.LogError("Failed to send an email to with response {@Response}", response);

        return SendSingleEmailResponse.Failed("Failed to send an email to the recipient.");
    }
}
