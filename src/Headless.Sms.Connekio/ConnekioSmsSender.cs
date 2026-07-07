// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Encodings.Web;
using Headless.Checks;
using Headless.Http;
using Headless.Sms.Connekio.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Sms.Connekio;

internal sealed class ConnekioSmsSender(
    IHttpClientFactory httpClientFactory,
    string httpClientName,
    IOptionsMonitor<ConnekioSmsOptions> optionsMonitor,
    string? optionsName,
    ILogger<ConnekioSmsSender> logger
) : ISmsSender, IBulkSmsSender
{
    private static readonly JsonSerializerOptions _JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = ConnekioJsonSerializerContext.Default,
    };

    // Snapshot for this instance's options name — never CurrentValue, which binds the default options and
    // would bleed configuration across keyed instances.
    private readonly ConnekioSmsOptions _options = optionsMonitor.Get(optionsName);
    private readonly Uri _singleSmsEndpoint = new(optionsMonitor.Get(optionsName).SingleSmsEndpoint);
    private readonly Uri _batchSmsEndpoint = new(optionsMonitor.Get(optionsName).BatchSmsEndpoint);

    // Credentials are fixed at construction, so build the (immutable) Basic auth header once instead of
    // re-interpolating + base64-encoding it on every send.
    private readonly AuthenticationHeaderValue _basicAuthHeader = AuthenticationHeaderFactory.CreateBasic(
        $"{optionsMonitor.Get(optionsName).UserName}:{optionsMonitor.Get(optionsName).Password}:{optionsMonitor.Get(optionsName).AccountId}"
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
                    JsonContent.Create(
                        new ConnekioSingleSmsRequest
                        {
                            AccountId = _options.AccountId,
                            Sender = _options.Sender,
                            Text = request.Text,
                            Msisdn = request.Destination.ToString(),
                        },
                        options: _JsonOptions
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
                    JsonContent.Create(
                        new ConnekioBatchSmsRequest
                        {
                            AccountId = _options.AccountId,
                            Sender = _options.Sender,
                            Text = request.Text,
                            MobileList =
                            [
                                .. request.Destinations.Select(r => new ConnekioRecipient { Msisdn = r.ToString() }),
                            ],
                        },
                        options: _JsonOptions
                    ),
                request.Destinations.Count,
                cancellationToken
            )
            .ConfigureAwait(false);

        return SendBulkSmsResponse.FromAggregate(request.Destinations, outcome);
    }

    // Both entry points share one try/catch so the never-throw contract (only OperationCanceledException and
    // argument-validation propagate) lives in a single place. JsonContent serializes UTF-8 straight into the
    // request stream during SendAsync — inside this guarded region — so a serialization fault is still reported
    // as a failed response rather than thrown, without the UTF-16 payload string StringContent required.
    private async ValueTask<SendSingleSmsResponse> _SendAsync(
        Uri endpoint,
        [InstantHandle] Func<HttpContent> contentFactory,
        int destinationCount,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var content = contentFactory();

            return await _PostAsync(endpoint, content, destinationCount, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            logger.LogSmsSendException(e, destinationCount);

            return SendSingleSmsResponse.FromException(e);
        }
    }

    private async ValueTask<SendSingleSmsResponse> _PostAsync(
        Uri endpoint,
        HttpContent content,
        int destinationCount,
        CancellationToken cancellationToken
    )
    {
        using var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint);
        requestMessage.Content = content;
        requestMessage.Headers.Authorization = _basicAuthHeader;

        using var httpClient = httpClientFactory.CreateClient(httpClientName);
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
