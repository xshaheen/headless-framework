// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Net;
using System.Net.Http.Headers;
using Headless.Checks;
using Headless.PushNotifications.Apns.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Apple Push Notification service (APNs) push notification service. Sends one HTTP/2 request per device token
/// with a cached ES256 provider token and maps each APNs answer onto a <see cref="PushNotificationResponse"/>.
/// </summary>
/// <remarks>
/// Per-token outcomes never throw: rejections, transport faults left after retries, and resilience rejections
/// such as an open circuit all become <see cref="PushNotificationResponseStatus.Failure"/>, so a multicast keeps
/// every result it already has. Only invalid input and caller cancellation throw.
/// </remarks>
internal sealed class ApnsPushNotificationService(
    IHttpClientFactory httpClientFactory,
    string httpClientName,
    ApnsTokenSource tokenSource,
    IOptionsMonitor<ApnsOptions> optionsMonitor,
    string? optionsName,
    ILogger<ApnsPushNotificationService> logger
) : IPushNotificationService
{
    private static readonly MediaTypeHeaderValue _JsonContentType = new("application/json");

    public async ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientIdentifier,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(clientIdentifier);

        var options = optionsMonitor.Get(optionsName);
        var payload = ApnsPayloadWriter.Write(request, options.PushType);
        var client = httpClientFactory.CreateClient(httpClientName);

        return await _SendAsync(client, options, clientIdentifier, payload, request.CollapseKey, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientIdentifiers,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrEmpty(clientIdentifiers);

        // Every token is checked before the first send, so one bad entry cannot cause a partial delivery.
        for (var i = 0; i < clientIdentifiers.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(clientIdentifiers[i]))
            {
                throw new ArgumentException(
                    $"The client identifier at index {i.ToString(CultureInfo.InvariantCulture)} is null, empty, or white space.",
                    nameof(clientIdentifiers)
                );
            }
        }

        var options = optionsMonitor.Get(optionsName);
        var payload = ApnsPayloadWriter.Write(request, options.PushType);
        var client = httpClientFactory.CreateClient(httpClientName);
        var responses = new PushNotificationResponse[clientIdentifiers.Count];

        // Each send writes its own slot, which keeps input order without sorting afterwards.
        await Parallel
            .ForAsync(
                0,
                clientIdentifiers.Count,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.MaxConcurrency,
                    CancellationToken = cancellationToken,
                },
                async (index, token) =>
                    responses[index] = await _SendAsync(
                            client,
                            options,
                            clientIdentifiers[index],
                            payload,
                            request.CollapseKey,
                            token
                        )
                        .ConfigureAwait(false)
            )
            .ConfigureAwait(false);

        var successCount = responses.Count(static r => r.IsSucceeded());

        return new BatchPushNotificationResponse
        {
            SuccessCount = successCount,
            FailureCount = responses.Length - successCount,
            Responses = responses,
        };
    }

    private async ValueTask<PushNotificationResponse> _SendAsync(
        HttpClient client,
        ApnsOptions options,
        string deviceToken,
        byte[] payload,
        string? collapseKey,
        CancellationToken cancellationToken
    )
    {
        // Generated here rather than read from the response so a success always carries a message id, even when
        // a proxy strips the echoed apns-id header.
        var apnsId = Guid.NewGuid().ToString("D");

        try
        {
            _EnsureSecureEndpoint(client.BaseAddress);

            var token = await tokenSource.GetTokenAsync(options, cancellationToken).ConfigureAwait(false);
            var (status, reason) = await _PostAsync(
                    client,
                    options,
                    deviceToken,
                    payload,
                    collapseKey,
                    apnsId,
                    token.Value,
                    cancellationToken
                )
                .ConfigureAwait(false);

            if (
                status == HttpStatusCode.Forbidden
                && string.Equals(reason, ApnsResponseMapper.ExpiredProviderTokenReason, StringComparison.Ordinal)
            )
            {
                // One retry only: the source re-mints once per rejected generation (and never sooner than Apple's
                // 20-minute update limit), so a second rejection is a persistent problem, not a stale token.
                var retryToken = await tokenSource
                    .InvalidateAsync(options, token.Generation, cancellationToken)
                    .ConfigureAwait(false);

                logger.LogProviderTokenExpired(token.Generation, retryToken.Generation);

                (status, reason) = await _PostAsync(
                        client,
                        options,
                        deviceToken,
                        payload,
                        collapseKey,
                        apnsId,
                        retryToken.Value,
                        cancellationToken
                    )
                    .ConfigureAwait(false);
            }

            var response = ApnsResponseMapper.Map(
                deviceToken,
                apnsId,
                status,
                reason,
                options.TreatBadDeviceTokenAsUnregistered
            );

            if (response.IsUnregistered() && logger.IsEnabled(LogLevel.Information))
            {
                logger.LogDeviceTokenUnregistered((int)status, reason ?? "no reason", _Mask(deviceToken));
            }
            else if (response.IsFailed() && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogNotificationRejected((int)status, reason ?? "no reason", _Mask(deviceToken));
            }

            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            // Everything else (transport faults left after retries, an open circuit, a rate-limiter rejection, a
            // timeout, a misconfigured endpoint) is this token's failure; throwing would discard the results a
            // multicast already has.
            if (logger.IsEnabled(LogLevel.Error))
            {
                logger.LogSendFailed(e, _Mask(deviceToken));
            }

            return PushNotificationResponse.Failed(deviceToken, ApnsResponseMapper.DescribeException(e));
        }
    }

    private static async ValueTask<(HttpStatusCode Status, string? Reason)> _PostAsync(
        HttpClient client,
        ApnsOptions options,
        string deviceToken,
        byte[] payload,
        string? collapseKey,
        string apnsId,
        string providerToken,
        CancellationToken cancellationToken
    )
    {
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"/3/device/{Uri.EscapeDataString(deviceToken)}", UriKind.Relative)
        );

        // APNs speaks only HTTP/2; requiring it exactly also lets a cleartext loopback endpoint use h2c.
        message.Version = HttpVersion.Version20;
        message.VersionPolicy = HttpVersionPolicy.RequestVersionExact;

        message.Headers.Authorization = new AuthenticationHeaderValue("bearer", providerToken);
        message.Headers.TryAddWithoutValidation(
            "apns-topic",
            options.PushType == ApnsPushType.Voip ? $"{options.BundleId}.voip" : options.BundleId
        );
        message.Headers.TryAddWithoutValidation(
            "apns-push-type",
            options.PushType == ApnsPushType.Voip ? "voip" : "alert"
        );
        message.Headers.TryAddWithoutValidation(
            "apns-priority",
            ((int)options.Priority).ToString(CultureInfo.InvariantCulture)
        );
        message.Headers.TryAddWithoutValidation("apns-id", apnsId);

        if (collapseKey is not null)
        {
            message.Headers.TryAddWithoutValidation("apns-collapse-id", collapseKey);
        }

        message.Content = new ByteArrayContent(payload) { Headers = { ContentType = _JsonContentType } };

        using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return (HttpStatusCode.OK, null);
        }

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        return (response.StatusCode, ApnsResponseMapper.ReadReason(body));
    }

    private static void _EnsureSecureEndpoint(Uri? baseAddress)
    {
        if (baseAddress is null)
        {
            throw new InvalidOperationException("The APNs HttpClient has no BaseAddress.");
        }

        // The request carries the bearer provider token and the payload, so cleartext is allowed only on loopback,
        // where a local test double serves h2c.
        if (
            !string.Equals(baseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !_IsLoopbackHost(baseAddress.Host)
        )
        {
            throw new InvalidOperationException(
                $"The APNs endpoint '{baseAddress}' is not HTTPS. Only a loopback endpoint may use cleartext HTTP."
            );
        }
    }

    // Decided from the host string rather than Uri.IsLoopback: concurrent first reads of IsLoopback on one shared
    // Uri instance were observed to return false for 127.0.0.1, which would fail sends to a valid loopback endpoint.
    private static bool _IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(host.AsSpan().Trim(['[', ']']), out var address) && IPAddress.IsLoopback(address);
    }

    private static string _Mask(string deviceToken)
    {
        return deviceToken.Length > 8 ? deviceToken[..8] + "***" : "***";
    }
}
