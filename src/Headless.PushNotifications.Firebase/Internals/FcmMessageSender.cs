// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.PushNotifications.Firebase.Internals;

/// <summary>
/// Default <see cref="IFcmMessageSender"/> backed by the FirebaseAdmin SDK. Creates a uniquely-named
/// <see cref="FirebaseApp"/> lazily on first send (so registration has no side effects and several hosts can
/// coexist in one process with different credentials), and disposes it with the container.
/// </summary>
/// <remarks>
/// <para>
/// The <c>optionsName</c> constructor argument is the setup-builder instance name (<see langword="null"/> for
/// the default unkeyed sender). The sender reads the options snapshot for its own name
/// (<c>IOptionsMonitor.Get(optionsName)</c>) so keyed settings never bleed across instances — a keyed sender must
/// not read <c>CurrentValue</c>, which binds the default.
/// </para>
/// <para>
/// The SDK already retries HTTP 503 and transport exceptions itself (up to 4 times, not configurable), so this
/// layer retries only what the SDK does not: <c>INTERNAL</c> (500) and <c>QUOTA_EXCEEDED</c> (429). A multicast
/// resends only the tokens that failed that way, never the whole batch, because FCM has no idempotency key and
/// every resend of a delivered message delivers it twice.
/// </para>
/// </remarks>
internal sealed class FcmMessageSender : IFcmMessageSender, IDisposable
{
    private const string _ApnsNormalPriority = "5";
    private const string _ApnsHighPriority = "10";

    private readonly ILogger<FcmMessageSender> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly FirebaseRetryOptions _retry;
    private readonly bool _senderIdMismatchIsUnregistered;
    private readonly Lazy<FirebaseMessaging> _messaging;
    private FirebaseApp? _app;

    /// <summary>Creates a sender for the <paramref name="optionsName"/> Firebase options.</summary>
    /// <param name="options">The Firebase options monitor.</param>
    /// <param name="optionsName">The named options to read; <see langword="null"/> for the default instance.</param>
    /// <param name="timeProvider">The clock that retry delays wait on.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="httpClientFactory">
    /// Test seam: the factory both the Firebase app and its credential build their HTTP clients from, so a test can
    /// run the real SDK over a fake transport. <see langword="null"/> in production, which uses the SDK default.
    /// </param>
    public FcmMessageSender(
        IOptionsMonitor<FirebaseOptions> options,
        string? optionsName,
        TimeProvider timeProvider,
        ILogger<FcmMessageSender> logger,
        HttpClientFactory? httpClientFactory = null
    )
    {
        Argument.IsNotNull(options);
        _timeProvider = Argument.IsNotNull(timeProvider);
        _logger = Argument.IsNotNull(logger);

        var snapshot = options.Get(optionsName);
        var json = snapshot.Json;
        _retry = snapshot.Retry;
        _senderIdMismatchIsUnregistered = snapshot.TreatSenderIdMismatchAsUnregistered;
        var appName = "Headless.PushNotifications.Firebase." + Guid.NewGuid().ToString("N");

        _messaging = new Lazy<FirebaseMessaging>(() =>
        {
            var credential = CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential();

            if (httpClientFactory is not null)
            {
                credential = credential.CreateWithHttpClientFactory(httpClientFactory);
            }

            _app = FirebaseApp.Create(
                new AppOptions
                {
                    Credential = credential,
                    HttpClientFactory = httpClientFactory ?? new HttpClientFactory(),
                },
                appName
            );

            return FirebaseMessaging.GetMessaging(_app);
        });
    }

    public async Task<PushNotificationResponse> SendAsync(
        FcmMessageContent content,
        string fid,
        CancellationToken cancellationToken
    )
    {
        // Built once, so every retry carries the same absolute APNs expiration as the first attempt.
        var message = BuildMessage(content, fid, _timeProvider.GetUtcNow());

        for (var retry = 0; ; retry++)
        {
            Exception failure;

            try
            {
                var messageId = await _messaging.Value.SendAsync(message, cancellationToken).ConfigureAwait(false);

                return PushNotificationResponse.Succeeded(fid, messageId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                // One token's outcome: a timeout, a credential error, or any other failure is a Failed result.
                failure = e;
            }

            if (_GetRetryDelay(failure, retry) is not { } delay)
            {
                return _ToFailedResponse(fid, failure, log: true);
            }

            _logger.LogRetryAttempt(retry + 1, delay.TotalSeconds, failure.Message);
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<PushNotificationResponse>> SendBatchAsync(
        FcmMessageContent content,
        IReadOnlyList<string> fids,
        CancellationToken cancellationToken
    )
    {
        var now = _timeProvider.GetUtcNow();
        var results = new PushNotificationResponse[fids.Count];
        IReadOnlyList<int> pending = [.. Enumerable.Range(0, fids.Count)];

        for (var retry = 0; ; retry++)
        {
            var failures = await _SendRoundAsync(content, fids, pending, now, cancellationToken).ConfigureAwait(false);

            var retryIndices = new List<int>();
            var roundDelay = TimeSpan.Zero;

            foreach (var (index, response, failure) in failures)
            {
                if (response is not null)
                {
                    results[index] = response;
                }
                else if (_GetRetryDelay(failure, retry) is { } delay)
                {
                    retryIndices.Add(index);
                    roundDelay = delay > roundDelay ? delay : roundDelay;
                }
                else
                {
                    results[index] = _ToFailedResponse(fids[index], failure, log: false);
                }
            }

            if (retryIndices.Count == 0)
            {
                return results;
            }

            // One wait per round, as long as the slowest token asked for, so no token is resent early.
            _logger.LogRetryAttempt(retry + 1, roundDelay.TotalSeconds, $"{retryIndices.Count} transient failures");
            await Task.Delay(roundDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
            pending = retryIndices;
        }
    }

    /// <summary>
    /// Sends one multicast to the <paramref name="pending"/> indices of <paramref name="fids"/> and returns, per
    /// index, either the final response (success or unregistered) or the failure to classify.
    /// </summary>
    private async Task<List<(int Index, PushNotificationResponse? Response, Exception? Failure)>> _SendRoundAsync(
        FcmMessageContent content,
        IReadOnlyList<string> fids,
        IReadOnlyList<int> pending,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var outcomes = new List<(int, PushNotificationResponse?, Exception?)>(pending.Count);
        BatchResponse? batch = null;
        Exception? batchFailure = null;

        try
        {
            var message = BuildMulticastMessage(content, [.. pending.Select(i => fids[i])], now);
            batch = await _messaging.Value.SendEachForMulticastAsync(message, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            // Reported below as a failure of every token in the round, never thrown.
            batchFailure = e;
            _logger.FailedToSendPushNotification(e, $"multicast:{pending.Count}");
        }

        // The SDK turns a cancelled send into a per-message failure rather than throwing, so the caller's
        // cancellation has to be observed here.
        cancellationToken.ThrowIfCancellationRequested();

        if (batch is not null && batch.Responses.Count != pending.Count)
        {
            throw new InvalidOperationException(
                $"Firebase response count ({batch.Responses.Count}) does not match FID count ({pending.Count})."
            );
        }

        for (var i = 0; i < pending.Count; i++)
        {
            var index = pending[i];
            var response = batch?.Responses[i];

            if (response is { IsSuccess: true })
            {
                outcomes.Add((index, PushNotificationResponse.Succeeded(fids[index], response.MessageId), null));
            }
            else if (_IsUnregistered(response?.Exception))
            {
                outcomes.Add((index, PushNotificationResponse.Unregistered(fids[index]), null));
            }
            else
            {
                outcomes.Add((index, null, (Exception?)response?.Exception ?? batchFailure));
            }
        }

        return outcomes;
    }

    private TimeSpan? _GetRetryDelay(Exception? failure, int retry)
    {
        return retry < _retry.MaxAttempts
            ? RetryHelper.GetRetryDelay(failure, retry, _retry.MaxDelay, _timeProvider)
            : null;
    }

    private bool _IsUnregistered(Exception? exception)
    {
        return exception is FirebaseMessagingException { MessagingErrorCode: { } code }
            && (
                code is MessagingErrorCode.Unregistered
                || (code is MessagingErrorCode.SenderIdMismatch && _senderIdMismatchIsUnregistered)
            );
    }

    private PushNotificationResponse _ToFailedResponse(string fid, Exception? failure, bool log)
    {
        if (_IsUnregistered(failure))
        {
            return PushNotificationResponse.Unregistered(fid);
        }

        if (log && failure is not null)
        {
            _logger.FailedToSendPushNotification(failure, _Mask(fid));
        }

        return PushNotificationResponse.Failed(fid, _Describe(failure));
    }

    internal static Message BuildMessage(FcmMessageContent content, string fid, DateTimeOffset now)
    {
        return new Message
        {
            Fid = fid,
            Data = content.Data,
            Notification = _BuildNotification(content),
            Android = _BuildAndroidConfig(content),
            Apns = _BuildApnsConfig(content, now),
        };
    }

    internal static MulticastMessage BuildMulticastMessage(
        FcmMessageContent content,
        IReadOnlyList<string> fids,
        DateTimeOffset now
    )
    {
        return new MulticastMessage
        {
            Fids = [.. fids],
            Data = content.Data,
            Notification = _BuildNotification(content),
            Android = _BuildAndroidConfig(content),
            Apns = _BuildApnsConfig(content, now),
        };
    }

    private static Notification? _BuildNotification(FcmMessageContent content)
    {
        // Without a notification block FCM delivers a data message, which the app handles in the background.
        return content.IsDataOnly ? null : new Notification { Title = content.Title, Body = content.Body };
    }

    private static AndroidConfig _BuildAndroidConfig(FcmMessageContent content)
    {
        // Android cannot clear a badge, so a zero count means "not set" there, as it does in FCM itself.
        var count = content.Badge is > 0 ? content.Badge : null;

        return new AndroidConfig
        {
            // High stays the default so callers that never set a priority keep today's delivery.
            Priority = content.Priority is PushNotificationPriority.Normal ? Priority.Normal : Priority.High,
            CollapseKey = content.CollapseKey,
            TimeToLive = content.TimeToLive,
            Notification =
                count is null && content.Sound is null
                    ? null
                    : new AndroidNotification { NotificationCount = count, Sound = content.Sound },
        };
    }

    private static ApnsConfig _BuildApnsConfig(FcmMessageContent content, DateTimeOffset now)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);

        if (content.CollapseKey is not null)
        {
            headers["apns-collapse-id"] = content.CollapseKey;
        }

        if (content.IsDataOnly)
        {
            // Apple requires priority 5 for a background (content-available) push and rejects 10 for it.
            headers["apns-priority"] = _ApnsNormalPriority;
        }
        else if (content.Priority is { } priority)
        {
            headers["apns-priority"] =
                priority is PushNotificationPriority.High ? _ApnsHighPriority : _ApnsNormalPriority;
        }

        if (content.TimeToLive is { } timeToLive)
        {
            // APNs reads 0 as "attempt once, do not store", so a zero lifetime must not become the current time.
            headers["apns-expiration"] =
                timeToLive == TimeSpan.Zero
                    ? "0"
                    : (now + timeToLive).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        return new ApnsConfig { Aps = _BuildAps(content), Headers = headers.Count == 0 ? null : headers };
    }

    private static Aps? _BuildAps(FcmMessageContent content)
    {
        if (content.IsDataOnly)
        {
            return new Aps { ContentAvailable = true };
        }

        if (content.Badge is null && content.Sound is null)
        {
            return null;
        }

        return new Aps { Badge = content.Badge, Sound = content.Sound };
    }

    private static string _Describe(Exception? exception)
    {
        // Prefer the FCM code, then the platform code the SDK always sets, then the exception type, so the
        // description never starts with an empty code.
        return exception switch
        {
            null => "Unknown error",
            FirebaseMessagingException { MessagingErrorCode: { } code } => $"{code}: {exception.Message}",
            FirebaseException firebase => $"{firebase.ErrorCode}: {exception.Message}",
            _ => $"{exception.GetType().Name}: {exception.Message}",
        };
    }

    private static string _Mask(string clientIdentifier)
    {
        return clientIdentifier.Length > 8 ? clientIdentifier[..8] + "***" : "***";
    }

    public void Dispose()
    {
        if (_messaging.IsValueCreated)
        {
            _app?.Delete();
        }
    }
}
