// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Amazon.SimpleNotificationService;

namespace Headless.Messaging.Aws;

/// <summary>
/// The SNS topic ARNs the consumers of one process have created, shared so each topic costs one CreateTopic call per
/// process instead of one per listening client: every consumer identity runs several clients, and each one resolves its
/// topics at topology and again at subscription, which multiplies control-plane calls into SNS throttling.
/// </summary>
internal sealed class SnsTopicArnCache
{
    // Lazy starts the create once even when GetOrAdd races; a failed create is removed so the next caller retries.
    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _creations = new(StringComparer.Ordinal);

    public async Task<string> GetOrCreateAsync(
        IAmazonSimpleNotificationService snsClient,
        string topicName,
        CancellationToken cancellationToken
    )
    {
        // Built up front so the caller can tell its own create from one an earlier caller left; Lazy runs it only when
        // it wins the slot, so a losing candidate costs nothing.
        var candidate = new Lazy<Task<string>>(
            () => _CreateAsync(snsClient, topicName),
            LazyThreadSafetyMode.ExecutionAndPublication
        );
        var creation = _creations.GetOrAdd(topicName, candidate);

        // A create that failed while every waiter had already cancelled is still cached; replace it once. Only an
        // earlier caller's entry is replaced, so a create that fails at once surfaces its failure instead of looping.
        if (
            !ReferenceEquals(creation, candidate)
            && creation.Value is { IsCompleted: true, IsCompletedSuccessfully: false }
        )
        {
            _creations.TryRemove(KeyValuePair.Create(topicName, creation));
            creation = _creations.GetOrAdd(topicName, candidate);
        }

        var task = creation.Value;

        try
        {
            // The shared create outlives one caller's cancellation, so a cancelled subscribe stops waiting without
            // failing the other clients that wait on the same create.
            return await task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (task.IsCompleted && !task.IsCompletedSuccessfully)
        {
            // Drop the failed create so the next attempt retries instead of rethrowing a cached failure.
            _creations.TryRemove(KeyValuePair.Create(topicName, creation));
            throw;
        }
    }

    /// <summary>
    /// Forgets the cached ARN of <paramref name="topicName"/>, so the next caller creates the topic again. Called when SNS
    /// reports a cached topic missing, which happens when it is deleted while the process runs: without this a
    /// resubscribe would keep using the stale ARN instead of recreating the topic.
    /// </summary>
    public void Forget(string topicName) => _creations.TryRemove(topicName, out _);

    // The request form carries the FIFO attributes a FIFO topic must be created with. CreateTopic is idempotent and
    // returns the ARN of an existing topic.
    private static async Task<string> _CreateAsync(IAmazonSimpleNotificationService snsClient, string topicName)
    {
        var response = await snsClient
            .CreateTopicAsync(topicName.ToSnsCreateTopicRequest(), CancellationToken.None)
            .ConfigureAwait(false);

        return response.TopicArn;
    }
}
