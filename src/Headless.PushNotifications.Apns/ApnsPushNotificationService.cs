// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Headless.Checks;
using Headless.PushNotifications.Apns.Internals;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.PushNotifications.Apns;

/// <summary>
/// Apple Push Notification service (APNs) push notification service. Sends one HTTP/2 request per device token,
/// authenticated with a cached ES256 provider token or with the provider certificate presented during the TLS
/// handshake, and maps each APNs answer onto an <see cref="ApnsSendResult"/>.
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
    IApnsAuthenticator authenticator,
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
        var prepared = _Prepare(notification, options);

        return await _SendOneAsync(options, deviceToken, prepared, notification.ApnsId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async ValueTask<ApnsBatchSendResult> SendMulticastAsync(
        IReadOnlyList<string> deviceTokens,
        ApnsNotification notification,
        CancellationToken cancellationToken = default
    )
    {
        _EnsureTokens(deviceTokens, nameof(deviceTokens));
        Argument.IsNotNull(notification);

        // APNs identifies each request by its apns-id, so one caller id cannot cover several tokens.
        if (notification.ApnsId is not null)
        {
            throw new ArgumentException(
                "An APNs multicast cannot use a caller-supplied ApnsId; every request needs its own id. Send to each token with SendAsync instead.",
                nameof(notification)
            );
        }

        var options = optionsMonitor.Get(optionsName);
        var prepared = _Prepare(notification, options);
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
        var prepared = _Prepare(_Convert(request, options), options);
        var result = await _SendOneAsync(options, clientIdentifier, prepared, apnsId: null, cancellationToken)
            .ConfigureAwait(false);

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
        var prepared = _Prepare(_Convert(request, options), options);
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

    private ApnsPreparedNotification _Prepare(ApnsNotification notification, ApnsOptions options)
    {
        var prepared = ApnsPayloadWriter.Prepare(notification, options, timeProvider);
        authenticator.EnsureSupported(prepared.Headers.PushType, nameof(notification));

        return prepared;
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

        var data = _ToJson(request.Data);

        if (!PushNotificationRequestValidation.IsDataOnly(request))
        {
            return new ApnsAlertNotification
            {
                Alert = new ApnsAlert { Title = request.Title, Body = request.Body },
                Badge = request.Badge,
                Sound = request.Sound is null ? null : ApnsSound.Named(request.Sound),
                Data = data,
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
                Data = data,
                Priority = priority,
                Expiration = expiration,
                CollapseId = request.CollapseKey,
            };
        }

        // Apple requires priority 5 for background pushes, so the request's priority does not apply here.
        return new ApnsBackgroundNotification
        {
            Data = data,
            Expiration = expiration,
            CollapseId = request.CollapseKey,
        };
    }

    private static JsonObject? _ToJson(IReadOnlyDictionary<string, string>? data)
    {
        if (data is null)
        {
            return null;
        }

        // The shared request carries string values only, because FCM data messages are string maps.
        var json = new JsonObject();

        foreach (var (key, value) in data)
        {
            json[key] = value;
        }

        return json;
    }

    #endregion

    #region Sending

    private async ValueTask<ApnsSendResult> _SendOneAsync(
        ApnsOptions options,
        string deviceToken,
        ApnsPreparedNotification prepared,
        Guid? apnsId,
        CancellationToken cancellationToken
    )
    {
        var client = httpClientFactory.CreateClient(httpClientName);

        return await _SendAsync(client, options, deviceToken, prepared, apnsId, cancellationToken)
            .ConfigureAwait(false);
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
                    results[index] = await _SendAsync(
                            client,
                            options,
                            deviceTokens[index],
                            prepared,
                            callerApnsId: null,
                            token
                        )
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
        Guid? callerApnsId,
        CancellationToken cancellationToken
    )
    {
        // Decided here rather than read from the response so a success always carries a message id, even when a
        // proxy strips the echoed apns-id header; the expired-token resend reuses it so APNs sees one notification.
        var apnsId = (callerApnsId ?? Guid.NewGuid()).ToString("D");

        // One activity per device send; the token and payload are never attached, because the token is a stable
        // device identifier and the payload is message content.
        using var activity = ApnsDiagnostics.Start("apns.send");
        activity?.SetTag(ApnsTags.PushType, prepared.Headers.PushType);
        activity?.SetTag(
            ApnsTags.Environment,
            options.Environment == ApnsEnvironment.Sandbox ? "sandbox" : "production"
        );

        var startedAt = timeProvider.GetTimestamp();
        ApnsSendResult result;

        try
        {
            _EnsureSecureEndpoint(client.BaseAddress);

            var credential = await authenticator.GetCredentialAsync(options, cancellationToken).ConfigureAwait(false);
            var answer = await _PostAsync(client, deviceToken, prepared, apnsId, credential, cancellationToken)
                .ConfigureAwait(false);

            if (
                answer.Status == HttpStatusCode.Forbidden
                && string.Equals(
                    answer.Error.Reason,
                    ApnsResponseMapper.ExpiredProviderTokenReason,
                    StringComparison.Ordinal
                )
                && await authenticator.RenewExpiredAsync(options, credential, cancellationToken).ConfigureAwait(false)
                    is { } retryCredential
            )
            {
                // One retry only: a second rejection of a renewed token is a persistent problem, not a stale token.
                logger.LogProviderTokenExpired(credential.Generation, retryCredential.Generation);

                answer = await _PostAsync(client, deviceToken, prepared, apnsId, retryCredential, cancellationToken)
                    .ConfigureAwait(false);
            }

            result = ApnsResponseMapper.CreateResult(
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

            // Two authentication rejections say the instance's key or token configuration is wrong, not that one
            // send failed, so they get their own error events instead of the per-token rejection warning.
            if (ApnsFailureClassifier.IsTokenConfigurationProblem(answer.Error.Reason))
            {
                logger.LogTokenConfigurationError(answer.Error.Reason!, _Mask(deviceToken));
            }
            else if (string.Equals(answer.Error.Reason, "TooManyProviderTokenUpdates", StringComparison.Ordinal))
            {
                logger.LogProviderTokenUpdatedTooOften();
            }
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

            result = new ApnsSendResult
            {
                // The duplicate risk of retrying a transport failure is documented on ApnsFailureKind.Transport;
                // FailureError keeps its documented "<ExceptionType>: <message>" shape.
                Response = PushNotificationResponse.Failed(deviceToken, ApnsResponseMapper.DescribeException(e)),
                FailureKind = ApnsFailureKind.Transport,
            };
        }

        if (result.Response.IsSucceeded())
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        else
        {
            activity?.SetStatus(
                ActivityStatusCode.Error,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{(int?)result.StatusCode ?? 0} {result.Reason ?? result.Response.FailureError}"
                )
            );
        }

        activity?.SetTag(ApnsTags.Outcome, _OutcomeTag(result));
        activity?.SetTag(ApnsTags.FailureKind, result.FailureKind is { } kind ? ApnsMetrics.ToTagValue(kind) : null);
        activity?.SetTag(ApnsTags.Reason, result.Reason);

        if (ApnsMetrics.AnyEnabled)
        {
            ApnsMetrics.RecordSend(result, prepared.Headers.PushType, options.Environment);
            ApnsMetrics.RecordSendDuration(
                timeProvider.GetElapsedTime(startedAt),
                prepared.Headers.PushType,
                options.Environment
            );
        }

        return result;
    }

    private static string _OutcomeTag(ApnsSendResult result)
    {
        return result.Response.Status switch
        {
            PushNotificationResponseStatus.Success => "succeeded",
            PushNotificationResponseStatus.Unregistered => "unregistered",
            _ => "failed",
        };
    }

    private static async ValueTask<ApnsAnswer> _PostAsync(
        HttpClient client,
        string deviceToken,
        ApnsPreparedNotification prepared,
        string apnsId,
        ApnsCredential credential,
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

        credential.Apply(message);

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

        return new ApnsAnswer(
            response.StatusCode,
            ApnsResponseMapper.ReadError(body) with
            {
                RetryAfter = _ReadRetryAfter(response.Headers),
            },
            uniqueId
        );
    }

    /// <summary>
    /// Reads the <c>Retry-After</c> header as a delay. Apple's response-header table does not list it, but a
    /// throttling answer may carry one; a date-form or unparsable value reads as absent rather than failing the
    /// send it was meant to guide.
    /// </summary>
    private static TimeSpan? _ReadRetryAfter(HttpResponseHeaders headers)
    {
        if (
            !headers.TryGetValues("Retry-After", out var values)
            || values.FirstOrDefault() is not { } text
            || !int.TryParse(text.AsSpan().Trim(), CultureInfo.InvariantCulture, out var seconds)
            || seconds < 0
        )
        {
            return null;
        }

        return TimeSpan.FromSeconds(seconds);
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

        // The request carries the payload and, in token mode, the bearer provider token, so cleartext is allowed
        // only on loopback, where a local test double serves h2c.
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
