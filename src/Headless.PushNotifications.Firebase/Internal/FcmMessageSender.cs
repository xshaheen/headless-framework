// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Http;
using Headless.Checks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.PushNotifications.Firebase.Internal;

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
    private const string _SendActivityName = "fcm.send";
    private const string _MulticastActivityName = "fcm.multicast";

    private readonly ILogger<FcmMessageSender> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly string _instance;
    private readonly FirebaseRetryOptions _retry;
    private readonly bool _senderIdMismatchIsUnregistered;
    private readonly Lazy<FirebaseMessaging> _messaging;
    private FirebaseApp? _app;

    /// <summary>Creates a sender for the <paramref name="optionsName"/> Firebase options.</summary>
    /// <param name="options">The Firebase options monitor.</param>
    /// <param name="optionsName">The named options to read; <see langword="null"/> for the default instance.</param>
    /// <param name="timeProvider">The clock that retry delays wait on and durations are measured with.</param>
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
        _instance = optionsName ?? FcmDiagnostics.DefaultInstanceName;
        _logger = Argument.IsNotNull(logger);

        var snapshot = options.Get(optionsName);
        var json = snapshot.Json;
        _retry = snapshot.Retry;
        _senderIdMismatchIsUnregistered = snapshot.TreatSenderIdMismatchAsUnregistered;
        var appName = "Headless.PushNotifications.Firebase." + Guid.NewGuid().ToString("N");

        _messaging = new Lazy<FirebaseMessaging>(() =>
        {
            GoogleCredential credential;

            try
            {
                credential = CredentialFactory.FromJson<ServiceAccountCredential>(json).ToGoogleCredential();
            }
            catch (Exception e)
            {
                // Marked so the failure classifies as an authentication problem, not by its general exception type.
                throw new FcmCredentialException(e);
            }

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

    public async Task<FcmSendResult> SendAsync(
        FcmMessage message,
        FcmTarget target,
        CancellationToken cancellationToken
    )
    {
        var sdkMessage = FcmMessageMapper.ToMessage(message, target);
        using var activity = FcmDiagnostics.ActivitySource.StartActivity(_SendActivityName, ActivityKind.Client);
        activity?.SetTag(FcmTags.Instance, _instance);
        activity?.SetTag(FcmTags.TargetKind, FcmTarget.ToTagValue(target.Kind));
        activity?.SetTag(FcmTags.DryRun, message.DryRun);

        FcmSendResult result;

        for (var retry = 0; ; retry++)
        {
            Exception failure;
            var started = _timeProvider.GetTimestamp();

            try
            {
                var messageId = await _messaging
                    .Value.SendAsync(sdkMessage, message.DryRun, cancellationToken)
                    .ConfigureAwait(false);

                FcmMetrics.RecordDuration(
                    _instance,
                    _timeProvider.GetElapsedTime(started),
                    target.Kind,
                    multicast: false
                );
                result = new FcmSendResult { Response = PushNotificationResponse.Succeeded(target.Value, messageId) };

                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "cancelled");

                throw;
            }
            catch (Exception e)
            {
                // One target's outcome: a timeout, a credential error, or any other failure is a result, not a throw.
                failure = e;
            }

            // The SDK can report a send the caller cancelled as FCM's error or a network failure rather than a
            // cancellation, so the caller's cancellation is checked here as well.
            if (cancellationToken.IsCancellationRequested)
            {
                activity?.SetStatus(ActivityStatusCode.Error, "cancelled");
                cancellationToken.ThrowIfCancellationRequested();
            }

            FcmMetrics.RecordDuration(_instance, _timeProvider.GetElapsedTime(started), target.Kind, multicast: false);
            result = FcmFailureClassifier.Classify(
                target.Value,
                failure,
                _senderIdMismatchIsUnregistered,
                _timeProvider
            );

            if (result.Response.IsUnregistered() || _GetRetryDelay(failure, retry) is not { } delay)
            {
                if (result.Response.IsFailed())
                {
                    _logger.FailedToSendPushNotification(failure, _Mask(target.Value));
                }

                break;
            }

            FcmMetrics.RecordRetry(_instance, result, target.Kind);
            activity?.SetTag(FcmTags.Retry, retry + 1);
            _logger.LogRetryAttempt(retry + 1, delay.TotalSeconds, failure.Message);
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        FcmMetrics.RecordSend(_instance, result, target.Kind);
        _CompleteActivity(activity, result);

        return result;
    }

    public async Task<IReadOnlyList<FcmSendResult>> SendBatchAsync(
        FcmMessage message,
        IReadOnlyList<string> fids,
        CancellationToken cancellationToken
    )
    {
        var results = new FcmSendResult[fids.Count];
        List<int> pending = [.. Enumerable.Range(0, fids.Count)];

        for (var retry = 0; ; retry++)
        {
            var retryIndices = new List<int>();
            var roundDelay = TimeSpan.Zero;

            using (var activity = _StartMulticastActivity(pending.Count, retry, message.DryRun))
            {
                var outcomes = await _SendRoundAsync(message, fids, pending, cancellationToken).ConfigureAwait(false);
                var successCount = 0;

                foreach (var (index, messageId, failure) in outcomes)
                {
                    if (messageId is not null)
                    {
                        results[index] = new FcmSendResult
                        {
                            Response = PushNotificationResponse.Succeeded(fids[index], messageId),
                        };
                        successCount++;

                        continue;
                    }

                    var result = FcmFailureClassifier.Classify(
                        fids[index],
                        failure,
                        _senderIdMismatchIsUnregistered,
                        _timeProvider
                    );

                    if (!result.Response.IsUnregistered() && _GetRetryDelay(failure, retry) is { } delay)
                    {
                        retryIndices.Add(index);
                        roundDelay = delay > roundDelay ? delay : roundDelay;
                        FcmMetrics.RecordRetry(_instance, result, FcmTargetKind.Token);
                    }
                    else
                    {
                        results[index] = result;
                    }
                }

                activity?.SetTag(FcmTags.SuccessCount, successCount);
            }

            if (retryIndices.Count == 0)
            {
                foreach (var result in results)
                {
                    FcmMetrics.RecordSend(_instance, result, FcmTargetKind.Token);
                }

                return results;
            }

            // One wait per round, as long as the slowest token asked for, so no token is resent early.
            _logger.LogRetryAttempt(retry + 1, roundDelay.TotalSeconds, $"{retryIndices.Count} transient failures");
            await Task.Delay(roundDelay, _timeProvider, cancellationToken).ConfigureAwait(false);
            pending = retryIndices;
        }
    }

    /// <summary>
    /// Sends one round to the <paramref name="pending"/> indices of <paramref name="fids"/> and returns, per index,
    /// either the message id FCM accepted it with or the failure to classify.
    /// </summary>
    private async Task<List<(int Index, string? MessageId, Exception? Failure)>> _SendRoundAsync(
        FcmMessage message,
        IReadOnlyList<string> fids,
        List<int> pending,
        CancellationToken cancellationToken
    )
    {
        var outcomes = new List<(int, string?, Exception?)>(pending.Count);
        BatchResponse? batch = null;
        Exception? batchFailure = null;
        var started = _timeProvider.GetTimestamp();

        try
        {
            var messages = FcmMessageMapper.ToMessages(message, pending.Select(i => fids[i]));
            batch = await _messaging
                .Value.SendEachAsync(messages, message.DryRun, cancellationToken)
                .ConfigureAwait(false);
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

        FcmMetrics.RecordDuration(
            _instance,
            _timeProvider.GetElapsedTime(started),
            FcmTargetKind.Token,
            multicast: true
        );

        // The SDK turns a cancelled send into a per-message failure rather than throwing, so the caller's
        // cancellation has to be observed here.
        cancellationToken.ThrowIfCancellationRequested();

        if (batch is not null && batch.Responses.Count != pending.Count)
        {
            // Responses cannot be matched to tokens by position, so none is trusted; every token of the round is
            // reported as failed rather than the whole call throwing and losing earlier rounds' results.
            batchFailure = new InvalidOperationException(
                $"Firebase response count ({batch.Responses.Count}) does not match FID count ({pending.Count})."
            );
            batch = null;
            _logger.FailedToSendPushNotification(batchFailure, $"multicast:{pending.Count}");
        }

        for (var i = 0; i < pending.Count; i++)
        {
            var response = batch?.Responses[i];

            outcomes.Add(
                response is { IsSuccess: true }
                    ? (pending[i], response.MessageId, null)
                    : (pending[i], null, (Exception?)response?.Exception ?? batchFailure)
            );
        }

        return outcomes;
    }

    private TimeSpan? _GetRetryDelay(Exception? failure, int retry)
    {
        return retry < _retry.MaxAttempts
            ? RetryHelper.GetRetryDelay(failure, retry, _retry.MaxDelay, _timeProvider)
            : null;
    }

    private Activity? _StartMulticastActivity(int batchSize, int retry, bool dryRun)
    {
        var activity = FcmDiagnostics.ActivitySource.StartActivity(_MulticastActivityName, ActivityKind.Client);

        activity?.SetTag(FcmTags.Instance, _instance);
        activity?.SetTag(FcmTags.TargetKind, FcmTarget.ToTagValue(FcmTargetKind.Token));
        activity?.SetTag(FcmTags.BatchSize, batchSize);
        activity?.SetTag(FcmTags.Retry, retry);
        activity?.SetTag(FcmTags.DryRun, dryRun);

        return activity;
    }

    private static void _CompleteActivity(Activity? activity, FcmSendResult result)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(FcmTags.Outcome, FcmMetrics.OutcomeTag(result.Response.Status));

        if (result.FailureKind is { } kind)
        {
            activity.SetTag(FcmTags.FailureKind, FcmMetrics.ToTagValue(kind));
            activity.SetTag(FcmTags.ErrorCode, result.ErrorCode ?? "none");
        }

        // An unregistered token is an answer the caller acts on, not an error of the send.
        if (result.Response.IsFailed())
        {
            activity.SetStatus(ActivityStatusCode.Error, FcmMetrics.ToTagValue(result.FailureKind!.Value));
        }
    }

    private static string _Mask(string clientIdentifier)
    {
        return clientIdentifier.Length > 8 ? clientIdentifier[..8] + "***" : "***";
    }

    public void Dispose()
    {
        // Idempotent: the container and a test may both dispose the sender, and the app must be deleted once.
        Interlocked.Exchange(ref _app, value: null)?.Delete();
    }
}
