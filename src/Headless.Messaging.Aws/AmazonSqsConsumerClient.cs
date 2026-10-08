// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using Amazon.Auth.AccessControlPolicy;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Checks;
using Headless.Messaging.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Aws;

internal sealed class AmazonSqsConsumerClient(
    string subscriptionName,
    byte concurrency,
    IOptions<AmazonSqsMessagingOptions> options,
    ILogger<AmazonSqsConsumerClient> logger,
    MessageLane lane = MessageLane.Bus,
    TimeProvider? timeProvider = null
) : IConsumerClient
{
    // Bounds DisposeAsync, which carries no shutdown budget of its own; the core passes its remaining budget to
    // ShutdownAsync instead.
    private static readonly TimeSpan _ShutdownDrainTimeout = TimeSpan.FromSeconds(30);

    // Bounds handing unsettled messages back on shutdown, so an unreachable broker cannot hold the host; what it
    // cannot release in time reappears once its visibility runs out.
    private static readonly TimeSpan _ReleaseTimeout = TimeSpan.FromSeconds(5);

    private readonly Lock _connectionLock = new();
    private readonly Lock _queueUrlsLock = new();
    private readonly AmazonSqsMessagingOptions _amazonSqsOptions = options.Value;
    private readonly ILogger _logger = logger;
    private readonly SemaphoreSlim _semaphore = new(concurrency);
    private readonly ConsumerPauseGate _pauseGate = new();
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private ImmutableArray<string> _queueUrls = [];

    // Every received message the core has not settled yet, keyed by receipt handle: the heartbeat extends their
    // visibility and shutdown releases what is left.
    private readonly ConcurrentDictionary<string, InflightSqsMessage> _unsettled = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<Task, byte> _inFlightHandlers = new();
    private readonly ConcurrentDictionary<string, SqsDeleteBatcher> _deleteBatchers = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _stopReceiving = new();
    private readonly CancellationTokenSource _stopHeartbeat = new();

    // Orders every visibility change of this client: a heartbeat call that read a message as unsettled finishes before a
    // reject or release of that message is sent, so the extension can never land after it and hide the message again.
    private readonly SemaphoreSlim _visibilityGate = new(1, 1);
    private Task? _heartbeat;
    private int _disposed;
    private string _queueUrl = string.Empty;

    private IAmazonSimpleNotificationService? _snsClient;
    private IAmazonSQS? _sqsClient;

    public Func<TransportMessage, object?, Task>? OnMessageCallback { get; set; }

    public Action<LogMessageEventArgs>? OnLogCallback { get; set; }

    public void AttachCallbacks(Func<TransportMessage, object?, Task>? onMessage, Action<LogMessageEventArgs>? onLog)
    {
        OnMessageCallback = onMessage;
        OnLogCallback = onLog;
    }

    public BrokerAddress BrokerAddress => new("aws_sqs", _queueUrl);

    public async ValueTask<ICollection<string>> FetchMessageNamesAsync(
        IEnumerable<string> messageNames,
        CancellationToken cancellationToken = default
    )
    {
        Argument.IsNotNull(messageNames);

        try
        {
            if (lane == MessageLane.Queue)
            {
                await _ConnectAsync(initSns: false, initSqs: true, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                var queueUrls = new List<string>();
                foreach (var topic in messageNames)
                {
                    queueUrls.Add(
                        await _ResolveQueueUrlAsync(AwsPhysicalAddress.QueueDestination(topic), cancellationToken)
                            .ConfigureAwait(false)
                    );
                }

                _SetQueueUrls([.. queueUrls]);

                return queueUrls;
            }

            // Externally managed topology already subscribes the queue to its topics, so there is nothing to resolve.
            if (!_amazonSqsOptions.AutoProvision)
            {
                return [.. messageNames];
            }

            await _ConnectAsync(initSns: true, initSqs: false, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            var topicArns = new List<string>();
            foreach (var topic in messageNames)
            {
                var createTopicRequest = AwsPhysicalAddress.BusTopic(topic).ToSnsCreateTopicRequest();

                var createTopicResponse = await _snsClient!
                    .CreateTopicAsync(createTopicRequest, cancellationToken)
                    .ConfigureAwait(false);

                topicArns.Add(createTopicResponse.TopicArn);
            }

            await _GenerateSqsAccessPolicyAsync(topicArns, cancellationToken).ConfigureAwait(false);

            return topicArns;
        }
        catch (AmazonServiceException exception)
        {
            throw _CreateProvisioningFailure("resolve destinations", exception);
        }
    }

    public async ValueTask SubscribeAsync(IEnumerable<string> topics, CancellationToken cancellationToken = default)
    {
        Argument.IsNotNull(topics);

        try
        {
            if (lane == MessageLane.Queue)
            {
                await _ConnectAsync(initSns: false, initSqs: true, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _SetQueueUrls([.. topics]);
                _ready.TrySetResult();
                return;
            }

            if (!_amazonSqsOptions.AutoProvision)
            {
                await _ConnectAsync(initSns: false, initSqs: true, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                _ready.TrySetResult();
                return;
            }

            await _ConnectAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            await _SubscribeToTopics(topics, cancellationToken).ConfigureAwait(false);
            _ready.TrySetResult();
        }
        catch (AmazonServiceException exception)
        {
            throw _CreateProvisioningFailure("subscribe destinations", exception);
        }
    }

    public ValueTask WaitUntilReadyAsync(CancellationToken cancellationToken = default)
    {
        return new ValueTask(_ready.Task.WaitAsync(cancellationToken));
    }

    public async ValueTask ListeningAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        await _ConnectAsync(initSns: lane == MessageLane.Bus, initSqs: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (lane == MessageLane.Bus && _GetQueueUrlsSnapshot().Length == 0)
        {
            _SetQueueUrls([_queueUrl]);
        }

        // Shutdown stops receiving on its own, before it drains, so a caller that never cancels cannot keep feeding a
        // client that is shutting down. The loop is drained with the handlers, so shutdown releases and disposes only
        // after it stopped handing out messages.
        CancellationTokenSource receiveCts;
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Shutdown marks the client disposed under the same lock, so a listen that loses the race returns instead of
        // touching the sources shutdown disposes, and one that wins is registered before shutdown drains.
        lock (_connectionLock)
        {
            if (_disposed != 0)
            {
                return;
            }

            receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stopReceiving.Token);
            _heartbeat ??= _RunHeartbeatAsync(_stopHeartbeat.Token);
            _TrackInFlight(listening.Task);
        }

        try
        {
            await _ReceiveLoopAsync(timeout, cancellationToken, receiveCts.Token).ConfigureAwait(false);
        }
        finally
        {
            receiveCts.Dispose();
            listening.TrySetResult();
        }
    }

    // Cancelled through receiveToken by the caller or by shutdown; cancellationToken tells the two apart.
    private async Task _ReceiveLoopAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken,
        CancellationToken receiveToken
    )
    {
        var retryDelay = TimeSpan.FromSeconds(1);

        while (true)
        {
            try
            {
                await _pauseGate.WaitIfPausedAsync(receiveToken).ConfigureAwait(false);

                (string QueueUrl, ReceiveMessageResponse Response)[] responses;
                try
                {
                    var snapshot = _GetQueueUrlsSnapshot();
                    responses = await Task.WhenAll(
                            snapshot.Select(queueUrl => _ReceiveMessagesAsync(queueUrl, receiveToken))
                        )
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (receiveToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.SqsReceiveFailed(ex, subscriptionName);
                    retryDelay = _NextBackoff(retryDelay);
                    await _timeProvider.Delay(retryDelay, receiveToken).ConfigureAwait(false);
                    continue;
                }

                retryDelay = TimeSpan.FromSeconds(1);

                var receivedAny = false;
                foreach (var (queueUrl, response) in responses)
                {
                    if (response?.Messages?.Count > 0)
                    {
                        receivedAny = true;
                        await _DispatchAsync(queueUrl, response.Messages, receiveToken).ConfigureAwait(false);
                    }
                }

                if (!receivedAny)
                {
                    await _timeProvider.Delay(timeout, receiveToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
                when (!cancellationToken.IsCancellationRequested && _stopReceiving.IsCancellationRequested)
            {
                // Shutdown stopped the loop; the caller did not cancel, so this is a normal end of listening.
                return;
            }
        }
    }

    // Every message of a receive is tracked before the first one is handed over: the ones still waiting for a free
    // handler are already invisible, and the heartbeat must keep them so until their turn comes.
    private async Task _DispatchAsync(
        string queueUrl,
        List<Amazon.SQS.Model.Message> messages,
        CancellationToken cancellationToken
    )
    {
        var received = new InflightSqsMessage[messages.Count];
        for (var i = 0; i < messages.Count; i++)
        {
            received[i] = new InflightSqsMessage(queueUrl, messages[i].ReceiptHandle);
            _unsettled[received[i].ReceiptHandle] = received[i];
        }

        var dispatched = 0;
        try
        {
            for (; dispatched < messages.Count; dispatched++)
            {
                var sqsMessage = messages[dispatched];
                var inflight = received[dispatched];

                if (concurrency > 0)
                {
                    await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    var handler = Task.Run(
                        async () =>
                        {
                            try
                            {
                                await _ConsumeAsync(inflight, sqsMessage).ConfigureAwait(false);
                            }
                            finally
                            {
                                _ReleaseSemaphore();
                            }
                        },
                        CancellationToken.None // Ensure semaphore release even if cancellation is requested during handler execution
                    );
                    _TrackInFlight(handler);
                    _ObserveBackgroundHandler(handler);
                }
                else
                {
                    var handler = _ConsumeAsync(inflight, sqsMessage);
                    _TrackInFlight(handler);
                    await handler.ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Never handed to the core: give them back now instead of leaving them hidden for the visibility timeout.
            await _ReleaseAsync(received[dispatched..]).ConfigureAwait(false);
            throw;
        }
    }

    private async Task _ConsumeAsync(InflightSqsMessage inflight, Amazon.SQS.Model.Message sqsMessage)
    {
        try
        {
            var (header, body) = await _ReadMessageAsync(sqsMessage, inflight).ConfigureAwait(false);

            if (header is null)
            {
                return;
            }

            // Overwrites any wire value so a producer cannot choose the address.
            header[Headers.TransportAddress] = inflight.QueueUrl;

            if (!_HasRequiredHeaders(header))
            {
                _logger.SqsMessageMissingRequiredHeaders();
                await CommitAsync(inflight, CancellationToken.None).ConfigureAwait(false);
                return;
            }

            var message = new TransportMessage(header, body != null ? Encoding.UTF8.GetBytes(body) : null);

            await OnMessageCallback!(message, inflight).ConfigureAwait(false);
        }
        finally
        {
            // The core settles inside the callback, so a message still tracked here was never settled; the heartbeat
            // must not keep it hidden for good.
            _unsettled.TryRemove(inflight.ReceiptHandle, out _);
        }
    }

    private async Task<(string QueueUrl, ReceiveMessageResponse Response)> _ReceiveMessagesAsync(
        string queueUrl,
        CancellationToken cancellationToken
    )
    {
        var request = new ReceiveMessageRequest(queueUrl)
        {
            WaitTimeSeconds = (int)_amazonSqsOptions.ReceiveWaitTime.TotalSeconds,
            VisibilityTimeout = (int)_amazonSqsOptions.VisibilityTimeout.TotalSeconds,
            MaxNumberOfMessages = SqsDeleteBatcher.MaxBatchSize,
            MessageAttributeNames = ["All"],
        };
        var response = await _sqsClient!.ReceiveMessageAsync(request, cancellationToken).ConfigureAwait(false);
        return (queueUrl, response);
    }

    private ImmutableArray<string> _GetQueueUrlsSnapshot()
    {
        lock (_queueUrlsLock)
        {
            return _queueUrls;
        }
    }

    private void _SetQueueUrls(ImmutableArray<string> queueUrls)
    {
        lock (_queueUrlsLock)
        {
            _queueUrls = queueUrls;
        }
    }

    private static TimeSpan _NextBackoff(TimeSpan current)
    {
        return CalculateNextBackoff(current, RandomNumberGenerator.GetInt32(0, 1001));
    }

    internal static TimeSpan CalculateNextBackoff(TimeSpan current, int jitterPermille)
    {
        // Floor at 200ms, ceiling at 30s — jittered exponential backoff for transient SQS errors.
        var floor = TimeSpan.FromMilliseconds(200);
        var ceiling = TimeSpan.FromSeconds(30);
        var doubled = TimeSpan.FromTicks(Math.Max(current.Ticks * 2, floor.Ticks));
        var capped = doubled > ceiling ? ceiling : doubled;
        var jitterWindowTicks = Math.Min(capped.Ticks - floor.Ticks, capped.Ticks / 4);
        var jitterTicks = jitterWindowTicks * Math.Clamp(jitterPermille, 0, 1000) / 1000;
        return capped - TimeSpan.FromTicks(jitterTicks);
    }

    private InvalidOperationException _CreateProvisioningFailure(string stage, AmazonServiceException exception)
    {
        var requiredActions =
            !_amazonSqsOptions.AutoProvision ? "sqs:GetQueueUrl, on a queue that already exists"
            : lane == MessageLane.Bus
                ? "sns:CreateTopic, sqs:CreateQueue, sqs:GetQueueAttributes, sqs:SetQueueAttributes, sns:Subscribe, sns:SetSubscriptionAttributes"
            : "sqs:CreateQueue";
        var errorCode = string.IsNullOrWhiteSpace(exception.ErrorCode) ? "unknown" : exception.ErrorCode;

        return new InvalidOperationException(
            $"AWS_MESSAGING_PROVISIONING_DENIED: Failed to {stage} for the {lane} lane and subscription '{subscriptionName}' "
                + $"({errorCode}). Verify region/service endpoints and grant the runtime identity only the required actions: "
                + $"{requiredActions}. "
                + (
                    _amazonSqsOptions.AutoProvision
                        ? "Auto-provisioning never widens IAM policy automatically."
                        : "AutoProvision is off, so the queue, its topics, and their subscriptions must be created first."
                )
        );
    }

    private void _TrackInFlight(Task task)
    {
        _inFlightHandlers[task] = 0;
        _ = task.ContinueWith(
            static (completed, state) => ((ConcurrentDictionary<Task, byte>)state!).TryRemove(completed, out _),
            _inFlightHandlers,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    private void _ObserveBackgroundHandler(Task task)
    {
        _ = task.ContinueWith(
            completedTask =>
            {
                var exception = completedTask.Exception?.GetBaseException();
                if (exception is not null)
                {
                    _logger.SqsMessageConsumeFailed(exception, subscriptionName);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
    }

    public async ValueTask CommitAsync(object? sender, CancellationToken cancellationToken = default)
    {
        var inflight = _GetInflightMessage(sender);

        // Stop extending first: an extension racing the delete would only fail on the deleted message.
        _unsettled.TryRemove(inflight.ReceiptHandle, out _);

        var batcher = _deleteBatchers.GetOrAdd(
            inflight.QueueUrl,
            static (queueUrl, client) => new SqsDeleteBatcher(client._sqsClient!, queueUrl, client._InvalidIdFormatLog),
            this
        );

        await batcher.DeleteAsync(inflight.ReceiptHandle, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask RejectAsync(object? sender, CancellationToken cancellationToken = default)
    {
        var inflight = _GetInflightMessage(sender);

        // Stop extending first, or the next heartbeat would hide the rejected message again for a full timeout.
        _unsettled.TryRemove(inflight.ReceiptHandle, out _);

        await _visibilityGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _sqsClient!
                .ChangeMessageVisibilityAsync(inflight.QueueUrl, inflight.ReceiptHandle, 3, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (MessageNotInflightException ex)
        {
            _MessageNotInflightLog(ex.Message);
        }
        finally
        {
            _visibilityGate.Release();
        }
    }

    private void _ReleaseSemaphore()
    {
        if (concurrency > 0)
        {
            try
            {
                _semaphore.Release();
            }
            catch (SemaphoreFullException)
            {
                // Defensive: ignore over-release
            }
            catch (ObjectDisposedException)
            {
                // A handler that outlived the shutdown drain finishes after the semaphore is gone; nothing waits on it.
            }
        }
    }

    public async ValueTask PauseAsync(CancellationToken cancellationToken = default)
    {
        await _pauseGate.PauseAsync().ConfigureAwait(false);
    }

    public async ValueTask ResumeAsync(CancellationToken cancellationToken = default)
    {
        await _pauseGate.ResumeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync()
    {
        return ShutdownAsync(_ShutdownDrainTimeout);
    }

    public async ValueTask ShutdownAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        lock (_connectionLock)
        {
            if (_disposed != 0)
            {
                return;
            }

            _disposed = 1;
        }

        _pauseGate.Release();
        _ready.TrySetCanceled(CancellationToken.None);
        await _stopReceiving.CancelAsync().ConfigureAwait(false);

        // Drain in-flight handlers before disposing the SQS client, so a running handler's delete or reject reaches the
        // broker. The heartbeat keeps running meanwhile, so a slow handler's message stays hidden while it finishes.
        // Bounded so a stuck handler cannot hold the host: one still running past the budget has its delete fail and the
        // message is redelivered (at-least-once).
        // Snapshots repeat until none is left: a receive that already won its handler slot when shutdown cancelled still
        // starts that handler, after the first snapshot, and the receive loop in the snapshot ends only after it did.
        var startedAt = _timeProvider.GetTimestamp();
        try
        {
            Task[] inFlight;
            while ((inFlight = _inFlightHandlers.Keys.ToArray()).Length > 0)
            {
                var remaining = timeout - _timeProvider.GetElapsedTime(startedAt);
                if (remaining <= TimeSpan.Zero)
                {
                    throw new TimeoutException("The shared messaging shutdown deadline has expired.");
                }

                await Task.WhenAll(inFlight)
                    .WaitAsync(remaining, _timeProvider, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            // Handler faults are already logged by _ObserveBackgroundHandler; disposal must never block or throw.
            _logger.SqsShutdownDrainIncomplete(ex, subscriptionName, _inFlightHandlers.Count);
        }

        await _stopHeartbeat.CancelAsync().ConfigureAwait(false);
        if (_heartbeat is { } heartbeat)
        {
            await heartbeat.ConfigureAwait(false);
        }

        // Whatever is still unsettled was never handed over or outlived the drain; release it for another consumer now
        // instead of after its visibility timeout.
        await _ReleaseAsync([.. _unsettled.Values]).ConfigureAwait(false);

        _sqsClient?.Dispose();
        _snsClient?.Dispose();
        _semaphore.Dispose();
        _stopReceiving.Dispose();
        _stopHeartbeat.Dispose();
        _visibilityGate.Dispose();
    }

    // One loop per client extends every unsettled message in batches of ten, instead of a timer per message. Every
    // third of the visibility timeout leaves two chances to extend a message before it would reappear.
    private async Task _RunHeartbeatAsync(CancellationToken cancellationToken)
    {
        var interval = _amazonSqsOptions.VisibilityTimeout / 3;
        var visibilitySeconds = (int)_amazonSqsOptions.VisibilityTimeout.TotalSeconds;

        while (true)
        {
            try
            {
                await _timeProvider.Delay(interval, cancellationToken).ConfigureAwait(false);

                foreach (var queue in _unsettled.Values.GroupBy(static m => m.QueueUrl, StringComparer.Ordinal))
                {
                    foreach (var chunk in queue.Chunk(SqsDeleteBatcher.MaxBatchSize))
                    {
                        await _ExtendAsync(queue.Key, chunk, visibilitySeconds, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // The next beat retries; a message reappears only if every beat within its timeout fails.
                _logger.SqsVisibilityHeartbeatFailed(ex, subscriptionName);
            }
        }
    }

    private async Task _ExtendAsync(
        string queueUrl,
        InflightSqsMessage[] chunk,
        int visibilitySeconds,
        CancellationToken cancellationToken
    )
    {
        await _visibilityGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Read under the gate: a message settled since the snapshot is left alone, and one settled from here on
            // waits for this call before its own visibility change is sent.
            var unsettled = Array.FindAll(chunk, message => _unsettled.ContainsKey(message.ReceiptHandle));
            if (unsettled.Length > 0)
            {
                await _ChangeVisibilityAsync(queueUrl, unsettled, visibilitySeconds, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            _visibilityGate.Release();
        }
    }

    private async Task _ChangeVisibilityAsync(
        string queueUrl,
        InflightSqsMessage[] messages,
        int visibilitySeconds,
        CancellationToken cancellationToken
    )
    {
        var entries = new List<ChangeMessageVisibilityBatchRequestEntry>(messages.Length);
        for (var i = 0; i < messages.Length; i++)
        {
            entries.Add(
                new ChangeMessageVisibilityBatchRequestEntry(
                    i.ToString(CultureInfo.InvariantCulture),
                    messages[i].ReceiptHandle
                )
                {
                    VisibilityTimeout = visibilitySeconds,
                }
            );
        }

        var response = await _sqsClient!
            .ChangeMessageVisibilityBatchAsync(queueUrl, entries, cancellationToken)
            .ConfigureAwait(false);

        // The SDK leaves an empty result list null.
        foreach (var failed in response.Failed ?? [])
        {
            var message = messages[int.Parse(failed.Id, CultureInfo.InvariantCulture)];

            // Stops extending a message SQS refused for a reason of the request, such as a receipt handle that is no
            // longer current, which a retry would only refuse again; it may be redelivered. A service-side fault is
            // retried by the next beat. A message settled meanwhile is no longer tracked, so it logs nothing.
            if (failed.SenderFault is true && _unsettled.TryRemove(message.ReceiptHandle, out _))
            {
                _logger.SqsVisibilityExtensionRefused(subscriptionName, failed.Code, failed.Message);
            }
        }
    }

    private async Task _ReleaseAsync(IReadOnlyList<InflightSqsMessage> messages)
    {
        if (messages.Count == 0 || _sqsClient is null)
        {
            return;
        }

        foreach (var message in messages)
        {
            _unsettled.TryRemove(message.ReceiptHandle, out _);
        }

        using var timeout = new CancellationTokenSource(_ReleaseTimeout, _timeProvider);

        try
        {
            foreach (var queue in messages.GroupBy(static m => m.QueueUrl, StringComparer.Ordinal))
            {
                foreach (var chunk in queue.Chunk(SqsDeleteBatcher.MaxBatchSize))
                {
                    await _visibilityGate.WaitAsync(timeout.Token).ConfigureAwait(false);
                    try
                    {
                        await _ChangeVisibilityAsync(queue.Key, chunk, visibilitySeconds: 0, timeout.Token)
                            .ConfigureAwait(false);
                    }
                    finally
                    {
                        _visibilityGate.Release();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Unreleased messages reappear once their visibility runs out.
            _logger.SqsReleaseFailed(ex, subscriptionName, messages.Count);
        }
    }

    // Asynchronous version of Connect to avoid blocking threads during queue creation
    private async Task _ConnectAsync(
        bool initSns = true,
        bool initSqs = true,
        CancellationToken cancellationToken = default
    )
    {
        // Fast path if already initialized for requested resources
        if ((initSns && _snsClient == null) || (initSqs && _sqsClient == null))
        {
            if (_snsClient == null && initSns)
            {
                lock (_connectionLock)
                {
                    _snsClient ??= AwsClientFactory.CreateSnsClient(_amazonSqsOptions);
                }
            }

            if (_sqsClient == null && initSqs)
            {
                lock (_connectionLock)
                {
                    _sqsClient ??= AwsClientFactory.CreateSqsClient(_amazonSqsOptions);
                }

                if (lane == MessageLane.Bus && string.IsNullOrWhiteSpace(_queueUrl))
                {
                    _queueUrl = await _ResolveQueueUrlAsync(
                            AwsPhysicalAddress.BusSubscriptionQueue(subscriptionName),
                            cancellationToken
                        )
                        .ConfigureAwait(false);
                }
            }
        }
    }

    #region private methods

    // CreateQueue is idempotent and returns the URL of an existing queue; with AutoProvision off the queue must exist.
    private async Task<string> _ResolveQueueUrlAsync(string queueName, CancellationToken cancellationToken)
    {
        if (!_amazonSqsOptions.AutoProvision)
        {
            var urlResponse = await _sqsClient!
                .GetQueueUrlAsync(queueName.NormalizeForSqsQueueName(), cancellationToken)
                .ConfigureAwait(false);
            return urlResponse.QueueUrl;
        }

        var createResponse = await _sqsClient!
            .CreateQueueAsync(queueName.ToSqsCreateQueueRequest(), cancellationToken)
            .ConfigureAwait(false);
        return createResponse.QueueUrl;
    }

    // Both lanes read one envelope: the Queue lane sends the header bag itself, and the Bus lane's SNS subscriptions use
    // raw message delivery, which hands the published body and its one bag attribute to SQS unchanged.
    private async Task<(Dictionary<string, string?>? Headers, string? Body)> _ReadMessageAsync(
        Amazon.SQS.Model.Message sqsMessage,
        InflightSqsMessage inflight
    )
    {
        try
        {
            return (SqsHeaderCodec.Decode(sqsMessage.MessageAttributes), sqsMessage.Body);
        }
        catch (JsonException exception)
        {
            _logger.SqsMessageDeserializationFailed(exception);
            await CommitAsync(inflight, CancellationToken.None).ConfigureAwait(false);
            return (null, null);
        }
    }

    private static bool _HasRequiredHeaders(Dictionary<string, string?> headers)
    {
        return headers.TryGetValue(Headers.MessageId, out var messageId)
            && !string.IsNullOrWhiteSpace(messageId)
            && headers.TryGetValue(Headers.MessageName, out var messageName)
            && !string.IsNullOrWhiteSpace(messageName);
    }

    private InflightSqsMessage _GetInflightMessage(object? sender)
    {
        return sender switch
        {
            InflightSqsMessage inflight => inflight,
            string receiptHandle => new InflightSqsMessage(_queueUrl, receiptHandle),
            _ => throw new InvalidOperationException("SQS commit state is missing the receipt handle."),
        };
    }

    private void _InvalidIdFormatLog(string exceptionMessage)
    {
        var logArgs = new LogMessageEventArgs { LogType = MqLogType.InvalidIdFormat, Reason = exceptionMessage };

        OnLogCallback?.Invoke(logArgs);
    }

    private void _MessageNotInflightLog(string exceptionMessage)
    {
        var logArgs = new LogMessageEventArgs { LogType = MqLogType.MessageNotInflight, Reason = exceptionMessage };

        OnLogCallback?.Invoke(logArgs);
    }

    private async Task _GenerateSqsAccessPolicyAsync(IEnumerable<string> topicArns, CancellationToken cancellationToken)
    {
        await _ConnectAsync(initSns: false, initSqs: true, cancellationToken: cancellationToken).ConfigureAwait(false);

        var queueAttributes = await _sqsClient!
            .GetAttributesAsync(_queueUrl)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        var sqsQueueArn = queueAttributes["QueueArn"];

        var policy =
            queueAttributes.TryGetValue("Policy", out var policyStr) && !string.IsNullOrEmpty(policyStr)
                ? Policy.FromJson(policyStr)
                : new Policy();

        var topicArnsToAllow = topicArns.Where(a => !policy.HasSqsPermission(a, sqsQueueArn)).ToList();

        if (topicArnsToAllow.Count == 0)
        {
            return;
        }

        policy.AddSqsPermissions(topicArnsToAllow, sqsQueueArn);

        var setAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { { "Policy", policy.ToJson() } };
        await _sqsClient
            .SetAttributesAsync(_queueUrl, setAttributes)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task _SubscribeToTopics(IEnumerable<string> topics, CancellationToken cancellationToken)
    {
        var queueAttributes = await _sqsClient!
            .GetAttributesAsync(_queueUrl)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);

        var sqsQueueArn = queueAttributes["QueueArn"];
        foreach (var topicArn in topics)
        {
            var request = new SubscribeRequest
            {
                TopicArn = topicArn,
                Protocol = "sqs",
                Endpoint = sqsQueueArn,
                // The consumer reads the published body and header bag directly, not SNS's JSON wrapper. Asked for at
                // creation, so no message reaches a new subscription wrapped.
                Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["RawMessageDelivery"] = "true" },
            };

            SubscribeResponse response;
            try
            {
                response = await _snsClient!.SubscribeAsync(request, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidParameterException)
            {
                // SNS refuses to resubscribe an existing subscription with different attributes; take it as it is and
                // switch it below.
                request.Attributes = null;
                response = await _snsClient!.SubscribeAsync(request, cancellationToken).ConfigureAwait(false);
            }

            // Subscribe returns an existing subscription without applying the requested attributes, and a wrapped
            // message would be deleted as malformed, so raw delivery is set on every subscription the consumer owns.
            await _snsClient
                .SetSubscriptionAttributesAsync(
                    response.SubscriptionArn,
                    "RawMessageDelivery",
                    "true",
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
    }

    #endregion
}

internal sealed record InflightSqsMessage(string QueueUrl, string ReceiptHandle);

internal static partial class AmazonSqsConsumerClientLog
{
    [LoggerMessage(
        EventId = 4200,
        Level = LogLevel.Error,
        Message = "Failed to deserialize SQS message. The malformed transport envelope was terminally deleted."
    )]
    public static partial void SqsMessageDeserializationFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 4202,
        Level = LogLevel.Error,
        Message = "Invalid SQS transport envelope: required Messaging headers are missing. The malformed transport envelope was terminally deleted."
    )]
    public static partial void SqsMessageMissingRequiredHeaders(this ILogger logger);

    [LoggerMessage(
        EventId = 4203,
        Level = LogLevel.Error,
        Message = "Error consuming message for subscription {SubscriptionName}"
    )]
    public static partial void SqsMessageConsumeFailed(
        this ILogger logger,
        Exception exception,
        string subscriptionName
    );

    [LoggerMessage(
        EventId = 4204,
        Level = LogLevel.Warning,
        Message = "Failed to receive SQS messages for subscription {SubscriptionName}; backing off before retry."
    )]
    public static partial void SqsReceiveFailed(this ILogger logger, Exception exception, string subscriptionName);

    [LoggerMessage(
        EventId = 4205,
        Level = LogLevel.Warning,
        Message = "Failed to extend SQS message visibility for subscription {SubscriptionName}; retrying on the next heartbeat."
    )]
    public static partial void SqsVisibilityHeartbeatFailed(
        this ILogger logger,
        Exception exception,
        string subscriptionName
    );

    [LoggerMessage(
        EventId = 4206,
        Level = LogLevel.Warning,
        Message = "SQS refused to extend a message's visibility for subscription {SubscriptionName} ({ErrorCode}: {ErrorMessage}); the message may be redelivered while it is still being handled."
    )]
    public static partial void SqsVisibilityExtensionRefused(
        this ILogger logger,
        string subscriptionName,
        string errorCode,
        string errorMessage
    );

    [LoggerMessage(
        EventId = 4207,
        Level = LogLevel.Warning,
        Message = "Failed to release {MessageCount} unsettled SQS messages for subscription {SubscriptionName} on shutdown; they reappear once their visibility timeout runs out."
    )]
    public static partial void SqsReleaseFailed(
        this ILogger logger,
        Exception exception,
        string subscriptionName,
        int messageCount
    );

    [LoggerMessage(
        EventId = 4208,
        Level = LogLevel.Warning,
        Message = "SQS consumer for subscription {SubscriptionName} stopped with {HandlerCount} handlers still running after the shutdown drain; their messages are redelivered."
    )]
    public static partial void SqsShutdownDrainIncomplete(
        this ILogger logger,
        Exception exception,
        string subscriptionName,
        int handlerCount
    );
}
