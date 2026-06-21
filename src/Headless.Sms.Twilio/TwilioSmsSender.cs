// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Twilio.Clients;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Headless.Sms.Twilio;

/// <summary>
/// SMS sender implementation using Twilio API.
/// </summary>
/// <remarks>
/// Accepts <see cref="ITwilioRestClient"/> for testability and multi-tenant support.
/// Register the client via DI or use <see cref="TwilioRestClient"/> directly.
/// </remarks>
public sealed class TwilioSmsSender(
    ITwilioRestClient client,
    IOptions<TwilioSmsOptions> optionsAccessor,
    ILogger<TwilioSmsSender> logger
) : ISmsSender
{
    private readonly TwilioSmsOptions _options = optionsAccessor.Value;

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        if (request.Destinations.Count > 1)
        {
            return SendSingleSmsResponse.Failed("Twilio only supports sending to one destination at a time");
        }

        try
        {
            var response = await MessageResource
                .CreateAsync(
                    to: new PhoneNumber(request.Destinations[0].ToString(hasPlusPrefix: true)),
                    from: new PhoneNumber(_options.PhoneNumber),
                    body: request.Text,
                    maxPrice: _options.MaxPrice,
                    client: client
                )
                .ConfigureAwait(false);

            if (!response.ErrorCode.HasValue)
            {
                return SendSingleSmsResponse.Succeeded();
            }

            logger.LogFailedToSendSms(request.Destinations.Count, response.ErrorCode);

            return SendSingleSmsResponse.Failed(
                response.ErrorMessage
                    ?? $"Twilio error code {response.ErrorCode.Value.ToString(CultureInfo.InvariantCulture)}"
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogSmsSendException(e, request.Destinations.Count);

            return SendSingleSmsResponse.Failed(e.Message);
        }
    }
}
