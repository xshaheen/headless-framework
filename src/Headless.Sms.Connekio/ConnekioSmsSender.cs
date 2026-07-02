// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using Headless.Checks;
using Headless.Http;
using Headless.Sms.Connekio.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Headless.Sms.Connekio;

internal sealed class ConnekioSmsSender(
    IHttpClientFactory httpClientFactory,
    IOptions<ConnekioSmsOptions> optionsAccessor,
    ILogger<ConnekioSmsSender> logger
) : ISmsSender, IBulkSmsSender
{
    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = ConnekioJsonSerializerContext.Default,
    };

    private readonly ConnekioSmsOptions _options = optionsAccessor.Value;
    private readonly Uri _singleSmsEndpoint = new(optionsAccessor.Value.SingleSmsEndpoint);
    private readonly Uri _batchSmsEndpoint = new(optionsAccessor.Value.BatchSmsEndpoint);

    // Credentials are fixed at construction, so build the (immutable) Basic auth header once instead of
    // re-interpolating + base64-encoding it on every send.
    private readonly AuthenticationHeaderValue _basicAuthHeader = AuthenticationHeaderFactory.CreateBasic(
        $"{optionsAccessor.Value.UserName}:{optionsAccessor.Value.Password}:{optionsAccessor.Value.AccountId}"
    );

    public async ValueTask<SendSingleSmsResponse> SendAsync(
        SendSingleSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destination);
        Argument.IsNotEmpty(request.Text);

        return await _SendAsync(
                _singleSmsEndpoint,
                () =>
                    JsonSerializer.Serialize(
                        new ConnekioSingleSmsRequest
                        {
                            AccountId = _options.AccountId,
                            Sender = _options.Sender,
                            Text = request.Text,
                            Msisdn = request.Destination.ToString(),
                        },
                        _JsonOptions
                    ),
                destinationCount: 1,
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    public async ValueTask<SendBulkSmsResponse> SendBulkAsync(
        SendBulkSmsRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(request);
        Argument.IsNotNull(request.Destinations);
        Argument.IsNotEmpty(request.Destinations);
        Argument.IsNotEmpty(request.Text);

        // Connekio has a dedicated batch endpoint that returns a single status, so the same outcome applies to
        // every recipient.
        var outcome = await _SendAsync(
                _batchSmsEndpoint,
                () =>
                    JsonSerializer.Serialize(
                        new ConnekioBatchSmsRequest
                        {
                            AccountId = _options.AccountId,
                            Sender = _options.Sender,
                            Text = request.Text,
                            MobileList = request
                                .Destinations.Select(r => new ConnekioRecipient { Msisdn = r.ToString() })
                                .ToList(),
                        },
                        _JsonOptions
                    ),
                request.Destinations.Count,
                cancellationToken
            )
            .ConfigureAwait(false);

        return SendBulkSmsResponse.FromAggregate(request.Destinations, outcome);
    }

    // Both entry points share one try/catch so the never-throw contract (only OperationCanceledException and
    // argument-validation propagate) lives in a single place. The payload is built inside the guarded region so
    // a serialization fault is reported as a failed response rather than thrown.
    private async ValueTask<SendSingleSmsResponse> _SendAsync(
        Uri endpoint,
        [InstantHandle] Func<string> payloadFactory,
        int destinationCount,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var payload = payloadFactory();

            return await _PostAsync(endpoint, payload, destinationCount, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogSmsSendException(e, destinationCount);

            // The standard resilience pipeline surfaces its timeout and open-circuit rejections as
            // Polly-specific exceptions; both are transport faults a retry may clear, so classify them
            // as transient instead of letting them fall through as Unknown.
            return e is TimeoutRejectedException or BrokenCircuitException
                ? SendSingleSmsResponse.FromException(e, SmsFailureKind.Transient)
                : SendSingleSmsResponse.FromException(e);
        }
    }

    private async ValueTask<SendSingleSmsResponse> _PostAsync(
        Uri endpoint,
        string payload,
        int destinationCount,
        CancellationToken cancellationToken
    )
    {
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint);
        requestMessage.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        requestMessage.Headers.Authorization = _basicAuthHeader;

        using var httpClient = httpClientFactory.CreateClient(SetupConnekio.HttpClientName);
        using var response = await httpClient.SendAsync(requestMessage, cancellationToken).ConfigureAwait(false);

        // A success status code is authoritative; only read the body to explain a failure.
        if (response.IsSuccessStatusCode)
        {
            return SendSingleSmsResponse.Succeeded();
        }

        var rawContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(rawContent))
        {
            logger.LogEmptyResponse();
        }
        else
        {
            logger.LogFailedToSendSms(destinationCount, response.StatusCode);
        }

        var error = string.IsNullOrWhiteSpace(rawContent) ? "Failed to send SMS using Connekio API" : rawContent;

        // Connekio publishes no machine-readable error contract; a 401 against its Basic-auth endpoints is the
        // only status with unambiguous meaning (credentials rejected). Everything else surfaces the raw body
        // without guessing a kind.
        var kind =
            response.StatusCode is HttpStatusCode.Unauthorized ? SmsFailureKind.AuthFailure : SmsFailureKind.Unknown;

        return SendSingleSmsResponse.Failed(error, kind);
    }
}
