// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security;
using System.Security.Cryptography;
using Framework.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Sms.Vodafone;

public sealed class VodafoneSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<VodafoneSmsOptions> optionsAccessor,
    ILogger<VodafoneSmsSender> logger
) : ISmsSender
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
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        using var httpClient = httpClientFactory.CreateClient("VodafoneSms");
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, _uri);
        requestMessage.Content = new StringContent(_BuildPayload(request), Encoding.UTF8, "application/xml");

        var response = await httpClient.SendAsync(requestMessage, cancellationToken).AnyContext();
        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken).AnyContext();

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            logger.LogError("Empty response from Vodafone API");

            return SendSingleSmsResponse.Failed("Failed to send.");
        }

        var isSuccess = rawContent.Contains("<Success>true</Success>", StringComparison.Ordinal);

        if (isSuccess)
        {
            return SendSingleSmsResponse.Succeeded();
        }

        logger.LogError(
            "Failed to send SMS using Vodafone API to {DestinationCount} recipients, StatusCode={StatusCode}",
            request.Destinations.Count,
            response.StatusCode
        );

        return SendSingleSmsResponse.Failed("Failed to send.");
    }

    private string _BuildPayload(SendSingleSmsRequest request)
    {
        var hashableKey = _BuildHashableKey(request);
        var secureHash = _ComputeHash(hashableKey);
        var recipients = request.IsBatch
            ? string.Join(',', request.Destinations.Select(d => SecurityElement.Escape(d.ToString())))
            : SecurityElement.Escape(request.Destinations[0].ToString());

        // Simulate XML payload building.
        return "<Payload>"
            + $"<AccountId>{SecurityElement.Escape(_options.AccountId)}</AccountId>"
            + $"<Password>{SecurityElement.Escape(_options.Password)}</Password>"
            + $"<SenderName>{SecurityElement.Escape(_options.Sender)}</SenderName>"
            + $"<SecureHash>{secureHash}</SecureHash>"
            + $"<Recipients>{recipients}</Recipients>"
            + $"<Message>{SecurityElement.Escape(request.Text)}</Message>"
            + "</Payload>";
    }

    private string _BuildHashableKey(SendSingleSmsRequest request)
    {
        var hashableKey = new StringBuilder();

        hashableKey.Append(
            CultureInfo.InvariantCulture,
            $"AccountId={_options.AccountId}&Password={_options.Password}"
        );

        if (request.IsBatch)
        {
            foreach (var recipient in request.Destinations)
            {
                hashableKey.Append(
                    CultureInfo.InvariantCulture,
                    $"&SenderName={_options.Sender}&ReceiverMSISDN={recipient}&SMSText={request.Text}"
                );
            }
        }
        else
        {
            hashableKey.Append(
                CultureInfo.InvariantCulture,
                $"&SenderName={_options.Sender}&ReceiverMSISDN={request.Destinations[0]}&SMSText={request.Text}"
            );
        }

        return hashableKey.ToString();
    }

    private string _ComputeHash(string input)
    {
        using var hmac = new HMACSHA256(_secureHash);
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
        var hashString = BitConverter.ToString(hashBytes);

        return hashString.RemoveCharacter('-').ToUpper(CultureInfo.CurrentCulture);
    }
}
