// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security;
using System.Security.Cryptography;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sms.Vodafone;

internal sealed class VodafoneSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<VodafoneSmsOptions> optionsAccessor,
    ILogger<VodafoneSmsSender> logger
) : ISmsSender, IBulkSmsSender
{
    private readonly VodafoneSmsOptions _options = optionsAccessor.Value;
    private readonly Uri _uri = new(optionsAccessor.Value.SendSmsEndpoint);
    private readonly byte[] _secureHash = Encoding.UTF8.GetBytes(optionsAccessor.Value.SecureHash);

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destination);
        Argument.IsNotEmpty(request.Text);

        return await _SendAsync([request.Destination], request.Text, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<SendBulkSmsResponse> SendBulkAsync(
        SendBulkSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        // Vodafone Egypt accepts a comma-separated recipient list and reports a single batch status, so the
        // same outcome applies to every recipient.
        var outcome = await _SendAsync(request.Destinations, request.Text, cancellationToken).ConfigureAwait(false);

        return SendBulkSmsResponse.FromAggregate(request.Destinations, outcome);
    }

    private async ValueTask<SendSingleSmsResponse> _SendAsync(
        IReadOnlyList<SmsRequestDestination> destinations,
        string text,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await _SendCoreAsync(destinations, text, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogSmsSendException(e, destinations.Count);

            return SendSingleSmsResponse.FromException(e);
        }
    }

    private async ValueTask<SendSingleSmsResponse> _SendCoreAsync(
        IReadOnlyList<SmsRequestDestination> destinations,
        string text,
        CancellationToken cancellationToken
    )
    {
        using var httpClient = httpClientFactory.CreateClient(SetupVodafone.HttpClientName);
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _uri);
        requestMessage.Content = new StringContent(_BuildPayload(destinations, text), Encoding.UTF8, "application/xml");

        using var response = await httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            logger.LogEmptyResponse();

            return SendSingleSmsResponse.Failed("Vodafone returned an empty response");
        }

        var isSuccess = rawContent.Contains("<Success>true</Success>", StringComparison.OrdinalIgnoreCase);

        if (isSuccess)
        {
            return SendSingleSmsResponse.Succeeded();
        }

        logger.LogFailedToSendSms(destinations.Count, response.StatusCode);

        // Vodafone reports logical failures inside a 200 body, so the HTTP status is not a reliable signal;
        // surface the raw response so callers can see the provider's actual error.
        return SendSingleSmsResponse.Failed(rawContent);
    }

    private string _BuildPayload(IReadOnlyList<SmsRequestDestination> destinations, string text)
    {
        var secureHash = _ComputeHash(_BuildHashableKey(destinations, text));
        var recipients = string.Join(',', destinations.Select(d => SecurityElement.Escape(d.ToString())));

        return "<Payload>"
            + $"<AccountId>{SecurityElement.Escape(_options.AccountId)}</AccountId>"
            + $"<Password>{SecurityElement.Escape(_options.Password)}</Password>"
            + $"<SenderName>{SecurityElement.Escape(_options.Sender)}</SenderName>"
            + $"<SecureHash>{secureHash}</SecureHash>"
            + $"<Recipients>{recipients}</Recipients>"
            + $"<Message>{SecurityElement.Escape(text)}</Message>"
            + "</Payload>";
    }

    private string _BuildHashableKey(IReadOnlyList<SmsRequestDestination> destinations, string text)
    {
        var hashableKey = new StringBuilder();

        hashableKey.Append(
            CultureInfo.InvariantCulture,
            $"AccountId={_options.AccountId}&Password={_options.Password}"
        );

        foreach (var recipient in destinations)
        {
            hashableKey.Append(
                CultureInfo.InvariantCulture,
                $"&SenderName={_options.Sender}&ReceiverMSISDN={recipient}&SMSText={text}"
            );
        }

        return hashableKey.ToString();
    }

    private string _ComputeHash(string input)
    {
        using var hmac = new HMACSHA256(_secureHash);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));

        return Convert.ToHexString(hashBytes);
    }
}
