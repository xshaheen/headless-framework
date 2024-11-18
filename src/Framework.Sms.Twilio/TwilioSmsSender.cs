// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Framework.Sms.Twilio;

public sealed class TwilioSmsSender : ISmsSender
{
    private readonly TwilioOptions _options;

    public TwilioSmsSender(IOptions<TwilioOptions> optionsAccessor)
    {
        _options = optionsAccessor.Value;
        TwilioClient.Init(_options.Sid, _options.AuthToken);
    }

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken token = default
    )
    {
        var respond = await MessageResource.CreateAsync(
            to: new PhoneNumber(request.Destination.ToString()),
            from: new PhoneNumber(_options.PhoneNumber),
            body: request.Text,
            maxPrice: 0.5m
        );

        return respond.ErrorCode.HasValue
            ? SendSingleSmsResponse.Failed(respond.ErrorMessage)
            : SendSingleSmsResponse.Succeeded();
    }
}
