// Copyright (c) Mahmoud Shaheen, 2024. All rights reserved

using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Framework.Sms.Twilio;

public sealed class TwilioSmsSender : ISmsSender
{
    private readonly TwilioSettings _settings;

    public TwilioSmsSender(IOptions<TwilioSettings> options)
    {
        _settings = options.Value;
        TwilioClient.Init(_settings.Sid, _settings.AuthToken);
    }

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken token = default
    )
    {
        var respond = await MessageResource.CreateAsync(
            to: new PhoneNumber(request.Destination.ToString()),
            from: new PhoneNumber(_settings.PhoneNumber),
            body: request.Text,
            maxPrice: 0.5m
        );

        return respond.ErrorCode.HasValue
            ? SendSingleSmsResponse.Failed(respond.ErrorMessage)
            : SendSingleSmsResponse.Succeeded();
    }
}
