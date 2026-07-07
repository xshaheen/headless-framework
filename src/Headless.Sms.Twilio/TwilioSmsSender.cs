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
internal sealed class TwilioSmsSender(
    ITwilioRestClient client,
    IOptionsMonitor<TwilioSmsOptions> optionsMonitor,
    string? optionsName,
    ILogger<TwilioSmsSender> logger
) : ISmsSender
{
    // Snapshot for this instance's options name — never CurrentValue, which binds the default options and
    // would bleed configuration across keyed instances.
    private readonly TwilioSmsOptions _options = optionsMonitor.Get(optionsName);

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destination);
        Argument.IsNotEmpty(request.Text);

        try
        {
            // The Twilio SDK (7.x) does not accept a CancellationToken on its send path, so cancellation can
            // only be honored up to the point of dispatch.
            cancellationToken.ThrowIfCancellationRequested();

            var response = await MessageResource
                .CreateAsync(
                    to: new PhoneNumber(request.Destination.ToString(hasPlusPrefix: true)),
                    from: new PhoneNumber(_options.PhoneNumber),
                    body: request.Text,
                    maxPrice: _options.MaxPrice,
                    client: client
                )
                .ConfigureAwait(false);

            if (!response.ErrorCode.HasValue)
            {
                return SendSingleSmsResponse.Succeeded(response.Sid);
            }

            logger.LogFailedToSendSms(destinationCount: 1, response.ErrorCode);

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
            logger.LogSmsSendException(e, destinationCount: 1);

            return SendSingleSmsResponse.FromException(e, SmsFailureKinds.FromException(e));
        }
    }
}
