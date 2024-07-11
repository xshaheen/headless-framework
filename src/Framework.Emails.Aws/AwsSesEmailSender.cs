using Amazon.SimpleEmailV2;
using Amazon.SimpleEmailV2.Model;
using Framework.Emails.Contracts;
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
        var sendEmailRequest = new SendEmailRequest
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
                    Subject = new Content { Charset = "UTF-8", Data = request.Subject },
                    Body = new Body
                    {
                        Html = string.IsNullOrWhiteSpace(request.MessageHtml)
                            ? default
                            : new Content { Charset = "UTF-8", Data = request.MessageHtml },
                        Text = string.IsNullOrWhiteSpace(request.MessageText)
                            ? default
                            : new Content { Charset = "UTF-8", Data = request.MessageText },
                    },
                },
            },
        };

        SendEmailResponse sendEmailResponse;

        try
        {
            sendEmailResponse = await _client.SendEmailAsync(sendEmailRequest, cancellationToken);
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

        if (sendEmailResponse.HttpStatusCode.IsSuccessStatusCode())
        {
            return SendSingleEmailResponse.Succeeded();
        }

        _logger.LogError("Failed to send an email {@Request} {@Response}", sendEmailRequest, sendEmailResponse);

        return SendSingleEmailResponse.Failed("Failed to send an email to the recipient.");
    }
}
