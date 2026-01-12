// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Framework.Checks;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;

namespace Framework.Sms.Twilio;

/// <summary>
/// SMS sender implementation using Twilio API.
/// </summary>
/// <remarks>
/// <para>
/// This implementation uses <see cref="TwilioClient.Init"/> which sets credentials
/// in global static state. Only one Twilio account per application is supported.
/// </para>
/// <para>
/// For multi-tenant scenarios or unit testing, consider injecting
/// <c>ITwilioRestClient</c> directly (not currently supported).
/// </para>
/// </remarks>
public sealed class TwilioSmsSender(IOptions<TwilioSmsOptions> optionsAccessor) : ISmsSender
{
    private readonly TwilioSmsOptions _options = optionsAccessor.Value;

    private static bool _initialized;
    private static readonly Lock _InitLock = new();

    private void _EnsureInitialized()
    {
        if (_initialized)
            return;

        lock (_InitLock)
        {
            if (_initialized)
                return;
            TwilioClient.Init(_options.Sid, _options.AuthToken);
            _initialized = true;
        }
    }

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        _EnsureInitialized();

        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        if (request.Destinations.Count > 1)
        {
            return SendSingleSmsResponse.Failed("Twilio only supports sending to one destination at a time");
        }

        var respond = await MessageResource
            .CreateAsync(
                to: new PhoneNumber(request.Destinations[0].ToString(hasPlusPrefix: true)),
                from: new PhoneNumber(_options.PhoneNumber),
                body: request.Text,
                maxPrice: _options.MaxPrice
            )
            .AnyContext();

        return respond.ErrorCode.HasValue
            ? SendSingleSmsResponse.Failed(respond.ErrorMessage)
            : SendSingleSmsResponse.Succeeded();
    }
}
