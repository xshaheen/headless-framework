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
/// with a cached ES256 provider token and maps each APNs answer onto an <see cref="ApnsSendResult"/>.
/// </summary>
/// <remarks>
/// <para>
/// One instance serves both <see cref="IApnsPushNotificationService"/> and <see cref="IPushNotificationService"/>. The
/// shared methods convert the request into an alert, background, or VoIP notification, send it through the typed
/// path, and return each result's <see cref="ApnsSendResult.Response"/>.
/// </para>
/// <para>
/// Per-token outcomes never throw: rejections, transport faults left after retries, and resilience rejections
/// such as an open circuit all become <see cref="PushNotificationResponseStatus.Failure"/>, so a multicast keeps
/// every result it already has. Only invalid input and caller cancellation throw.
/// </para>
/// </remarks>
internal sealed class ApnsPushNotificationService(
    IHttpClientFactory httpClientFactory,
    string httpClientName,
    ApnsTokenSource tokenSource,
    IOptionsMonitor<ApnsOptions> optionsMonitor,
    string? optionsName,
    TimeProvider timeProvider,
    ILogger<ApnsPushNotificationService> logger
) : IApnsPushNotificationService, IPushNotificationService
{
    private const string _UniqueIdHeader = "apns-unique-id";

    private static readonly MediaTypeHeaderValue _JsonContentType = new("application/json");

    #region Typed

    public async ValueTask<ApnsSendResult> SendAsync(
        string deviceToken,
        ApnsNotification notification,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(deviceToken);

        var options = optionsMonitor.Get(optionsName);
        var prepared = ApnsPayloadWriter.Prepare(notification, options, timeProvider);

        return await _SendOneAsync(options, deviceToken, prepared, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<ApnsBatchSendResult> SendMulticastAsync(
        IReadOnlyList<string> deviceTokens,
        ApnsNotification notification,
        CancellationToken cancellationToken = default
    )
    {
        _EnsureTokens(deviceTokens, nameof(deviceTokens));

        var options = optionsMonitor.Get(optionsName);
        var prepared = ApnsPayloadWriter.Prepare(notification, options, timeProvider);
        var results = await _SendManyAsync(options, deviceTokens, prepared, cancellationToken).ConfigureAwait(false);
        var successCount = results.Count(static r => r.Response.IsSucceeded());

        return new ApnsBatchSendResult
        {
            SuccessCount = successCount,
            FailureCount = results.Length - successCount,
            Results = results,
        };
    }

    #endregion

    #region Shared

    public async ValueTask<PushNotificationResponse> SendToDeviceAsync(
        string clientIdentifier,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNullOrWhiteSpace(clientIdentifier);

        var options = optionsMonitor.Get(optionsName);
        var prepared = ApnsPayloadWriter.Prepare(_Convert(request, options), options, timeProvider);
        var result = await _SendOneAsync(options, clientIdentifier, prepared, cancellationToken).ConfigureAwait(false);

        return result.Response;
    }

    public async ValueTask<BatchPushNotificationResponse> SendMulticastAsync(
        IReadOnlyList<string> clientIdentifiers,
        PushNotificationRequest request,
        CancellationToken cancellationToken = default
    )
    {
        _EnsureTokens(clientIdentifiers, nameof(clientIdentifiers));

        var options = optionsMonitor.Get(optionsName);
        var prepared = ApnsPayloadWriter.Prepare(_Convert(request, options), options, timeProvider);
        var results = await _SendManyAsync(options, clientIdentifiers, prepared, cancellationToken)
            .ConfigureAwait(false);
        var responses = results.Select(static r => r.Response).ToArray();
        var successCount = responses.Count(static r => r.IsSucceeded());

        return new BatchPushNotificationResponse
        {
            SuccessCount = successCount,
            FailureCount = responses.Length - successCount,
            Responses = responses,
        };
    }

    /// <summary>Converts a validated shared request into the APNs notification it becomes on this instance.</summary>
    private ApnsNotification _Convert(PushNotificationRequest request, ApnsOptions options)
    {
        PushNotificationRequestValidation.Validate(request);

        var expiration = request.TimeToLive switch
        {
            null => null,
            { } ttl when ttl == TimeSpan.Zero => ApnsExpiration.DeliverOnce,
            { } ttl => ApnsExpiration.At(timeProvider.GetUtcNow() + ttl),
        };

        ApnsPriority? priority = request.Priority switch
        {
            PushNotificationPriority.High => ApnsPriority.Immediate,
            PushNotificationPriority.Normal => ApnsPriority.PowerConsiderate,
            _ => null,
        };

        if (!PushNotificationRequestValidation.IsDataOnly(request))
        {
            return new ApnsAlertNotification
            {
                Alert = new ApnsAlert { Title = request.Title, Body = request.Body },
                Badge = request.Badge,
                Sound = request.Sound is null ? null : ApnsSound.Named(request.Sound),
                Data = request.Data,
                Priority = priority,
                Expiration = expiration,
                CollapseId = request.CollapseKey,
            };
        }

        // PushKit never receives a background push, so a VoIP instance keeps a data-only request a VoIP push.
        if (options.PushType == ApnsPushType.Voip)
        {
            return new ApnsVoipDataNotification
            {
                Data = request.Data,
                Priority = priority,
                Expiration = expiration,
                CollapseId = request.CollapseKey,
            };
        }

        // Apple requires priority 5 for background pushes, so the request's priority does not apply here.
        return new ApnsBackgroundNotification
        {
            Data = request.Data,
            Expiration = expiration,
            CollapseId = request.CollapseKey,
        };
    }

    #endregion

    #region Sending

    private async ValueTask<ApnsSendResult> _SendOneAsync(
        ApnsOptions options,
        string deviceToken,
        ApnsPreparedNotification prepared,
        CancellationToken cancellationToken
    )
    {
        var client = httpClientFactory.CreateClient(httpClientName);

        return await _SendAsync(client, options, deviceToken, prepared, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<ApnsSendResult[]> _SendManyAsync(
        ApnsOptions options,
        IReadOnlyList<string> deviceTokens,
        ApnsPreparedNotification prepared,
        CancellationToken cancellationToken
    )
    {
        var client = httpClientFactory.CreateClient(httpClientName);
        var results = new ApnsSendResult[deviceTokens.Count];

        // Each send writes its own slot, which keeps input order without sorting afterwards.
        await Parallel
            .ForAsync(
                0,
                deviceTokens.Count,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.MaxConcurrency,
                    CancellationToken = cancellationToken,
                },
                async (index, token) =>
                    results[index] = await _SendAsync(client, options, deviceTokens[index], prepared, token)
                        .ConfigureAwait(false)
            )
            .ConfigureAwait(false);

        return results;
    }

    private async ValueTask<ApnsSendResult> _SendAsync(
        HttpClient client,
        ApnsOptions options,
        string deviceToken,
        ApnsPreparedNotification prepared,
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
            var answer = await _PostAsync(client, deviceToken, prepared, apnsId, token.Value, cancellationToken)
                .ConfigureAwait(false);

            if (
                answer.Status == HttpStatusCode.Forbidden
                && string.Equals(
                    answer.Error.Reason,
                    ApnsResponseMapper.ExpiredProviderTokenReason,
                    StringComparison.Ordinal
                )
            )
            {
                // One retry only: the source re-mints once per rejected generation (and never sooner than Apple's
                // 20-minute update limit), so a second rejection is a persistent problem, not a stale token.
                var retryToken = await tokenSource
                    .InvalidateAsync(options, token.Generation, cancellationToken)
                    .ConfigureAwait(false);

                logger.LogProviderTokenExpired(token.Generation, retryToken.Generation);

                answer = await _PostAsync(client, deviceToken, prepared, apnsId, retryToken.Value, cancellationToken)
                    .ConfigureAwait(false);
            }

            var result = ApnsResponseMapper.CreateResult(
                deviceToken,
                apnsId,
                answer.Status,
                answer.Error,
                answer.UniqueId,
                options.TreatBadDeviceTokenAsUnregistered
            );

            if (result.Response.IsUnregistered() && logger.IsEnabled(LogLevel.Information))
            {
                logger.LogDeviceTokenUnregistered(
                    (int)answer.Status,
                    answer.Error.Reason ?? "no reason",
                    _Mask(deviceToken)
                );
            }
            else if (result.Response.IsFailed() && logger.IsEnabled(LogLevel.Warning))
            {
                logger.LogNotificationRejected(
                    (int)answer.Status,
                    answer.Error.Reason ?? "no reason",
                    _Mask(deviceToken)
                );
            }

            return result;
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

            return new ApnsSendResult
            {
                Response = PushNotificationResponse.Failed(deviceToken, ApnsResponseMapper.DescribeException(e)),
            };
        }
    }

    private static async ValueTask<ApnsAnswer> _PostAsync(
        HttpClient client,
        string deviceToken,
        ApnsPreparedNotification prepared,
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

        foreach (var (name, value) in prepared.Headers.Enumerate())
        {
            message.Headers.TryAddWithoutValidation(name, value);
        }

        message.Headers.TryAddWithoutValidation("apns-id", apnsId);
        message.Content = new ByteArrayContent(prepared.Payload) { Headers = { ContentType = _JsonContentType } };

        using var response = await client.SendAsync(message, cancellationToken).ConfigureAwait(false);

        var uniqueId = response.Headers.TryGetValues(_UniqueIdHeader, out var values) ? values.FirstOrDefault() : null;

        if (response.StatusCode == HttpStatusCode.OK)
        {
            return new ApnsAnswer(HttpStatusCode.OK, ApnsResponseMapper.ReadError([]), uniqueId);
        }

        var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        return new ApnsAnswer(response.StatusCode, ApnsResponseMapper.ReadError(body), uniqueId);
    }

    #endregion

    private static void _EnsureTokens(IReadOnlyList<string> deviceTokens, string paramName)
    {
        Argument.IsNotNullOrEmpty(deviceTokens, paramName: paramName);

        // Every token is checked before the first send, so one bad entry cannot cause a partial delivery.
        for (var i = 0; i < deviceTokens.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(deviceTokens[i]))
            {
                throw new ArgumentException(
                    $"The client identifier at index {i.ToString(CultureInfo.InvariantCulture)} is null, empty, or white space.",
                    paramName
                );
            }
        }
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

    private readonly record struct ApnsAnswer(HttpStatusCode Status, ApnsErrorBody Error, string? UniqueId);
}
