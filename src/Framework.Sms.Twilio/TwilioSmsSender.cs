// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Framework.Sms.Twilio;

public sealed class TwilioSmsSender : ISmsSender
{
    private readonly TwilioSmsOptions _options;

    public TwilioSmsSender(IOptions<TwilioSmsOptions> optionsAccessor)
    {
        _options = optionsAccessor.Value;
        TwilioClient.Init(_options.Sid, _options.AuthToken);
    }

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken token = default
    )
    {
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        if (request.Destinations.Count > 1)
        {
            return SendSingleSmsResponse.Failed("Twilio only supports sending to one destination at a time");
        }

        var respond = await MessageResource.CreateAsync(
            to: new PhoneNumber(request.Destinations.ToString()),
            from: new PhoneNumber(_options.PhoneNumber),
            body: request.Text,
            maxPrice: _options.MaxPrice
        );

        return respond.ErrorCode.HasValue
            ? SendSingleSmsResponse.Failed(respond.ErrorMessage)
            : SendSingleSmsResponse.Succeeded();
    }
}
