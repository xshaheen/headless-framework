// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Auth.AccessControlPolicy;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Hosting;

namespace Headless.Messaging.Aws;

/// <summary>
/// Creates and looks up the SNS topics, SQS queues, queue policies, and SNS-to-SQS subscriptions the transport uses, so
/// <c>resources provision</c>, a runtime subscription, and the running host ask AWS for the same objects with the same
/// attributes.
/// </summary>
/// <remarks>
/// The <c>Create*</c> and <c>Allow*</c>/<c>Subscribe*</c> methods write, for <c>resources provision</c> and runtime
/// subscriptions; <see cref="RequireQueueUrlAsync"/> only reads, and fails with a
/// <see cref="ResourceProvisioningException"/> naming <c>resources provision</c> when the queue does not exist.
/// </remarks>
internal static class AwsTopology
{
    private const string _RawMessageDelivery = "RawMessageDelivery";

    /// <summary>Creates the queue, with the FIFO attributes a <c>.fifo</c> name needs, and returns its URL.</summary>
    /// <remarks>CreateQueue is idempotent and returns the URL of an existing queue with the same attributes.</remarks>
    public static async Task<string> CreateQueueAsync(
        IAmazonSQS sqs,
        string queueName,
        CancellationToken cancellationToken
    )
    {
        var response = await sqs.CreateQueueAsync(queueName.ToSqsCreateQueueRequest(), cancellationToken)
            .ConfigureAwait(false);

        return response.QueueUrl;
    }

    /// <summary>Looks the queue up and returns its URL, without creating it.</summary>
    /// <exception cref="ResourceProvisioningException">The queue does not exist.</exception>
    public static async Task<string> RequireQueueUrlAsync(
        IAmazonSQS sqs,
        string queueName,
        CancellationToken cancellationToken
    )
    {
        return await TryGetQueueUrlAsync(sqs, queueName, cancellationToken).ConfigureAwait(false)
            ?? throw Missing($"SQS queue '{queueName.NormalizeForSqsQueueName()}'");
    }

    /// <summary>Looks the queue up and returns its URL, or <see langword="null"/> when it does not exist.</summary>
    public static async Task<string?> TryGetQueueUrlAsync(
        IAmazonSQS sqs,
        string queueName,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var response = await sqs.GetQueueUrlAsync(queueName.NormalizeForSqsQueueName(), cancellationToken)
                .ConfigureAwait(false);

            return response.QueueUrl;
        }
        catch (QueueDoesNotExistException)
        {
            return null;
        }
    }

    /// <summary>Creates the topic, with the FIFO attributes a <c>.fifo</c> name needs, and returns its ARN.</summary>
    /// <remarks>CreateTopic is idempotent and returns the ARN of an existing topic with the same attributes.</remarks>
    public static async Task<string> CreateTopicAsync(
        IAmazonSimpleNotificationService sns,
        string topicName,
        CancellationToken cancellationToken
    )
    {
        var response = await sns.CreateTopicAsync(topicName.ToSnsCreateTopicRequest(), cancellationToken)
            .ConfigureAwait(false);

        return response.TopicArn;
    }

    /// <summary>
    /// Lists every topic of the account and region, keyed by name. SNS has no lookup by name, so this costs one call per
    /// 100 topics in the account.
    /// </summary>
    public static async Task<Dictionary<string, string>> ListTopicArnsAsync(
        IAmazonSimpleNotificationService sns,
        CancellationToken cancellationToken
    )
    {
        var topics = new Dictionary<string, string>(StringComparer.Ordinal);
        string? nextToken = null;

        do
        {
            var page = await sns.ListTopicsAsync(new ListTopicsRequest { NextToken = nextToken }, cancellationToken)
                .ConfigureAwait(false);

            // The SDK leaves Topics null for a page with no topics, such as an account with none yet.
            foreach (var topic in page.Topics ?? [])
            {
                topics[topic.TopicArn.Split(':')[^1]] = topic.TopicArn;
            }

            nextToken = page.NextToken;
        } while (!string.IsNullOrEmpty(nextToken));

        return topics;
    }

    /// <summary>Reads the queue's ARN and its access policy in one call.</summary>
    public static async Task<(string QueueArn, Policy Policy)> GetQueueArnAndPolicyAsync(
        IAmazonSQS sqs,
        string queueUrl,
        CancellationToken cancellationToken
    )
    {
        var response = await sqs.GetQueueAttributesAsync(
                new GetQueueAttributesRequest { QueueUrl = queueUrl, AttributeNames = ["QueueArn", "Policy"] },
                cancellationToken
            )
            .ConfigureAwait(false);
        var attributes = response.Attributes ?? [];
        var policy =
            attributes.TryGetValue("Policy", out var json) && !string.IsNullOrEmpty(json)
                ? Policy.FromJson(json)
                : new Policy();

        return (attributes["QueueArn"], policy);
    }

    /// <summary>
    /// Grants each topic that the queue's policy does not allow yet <c>sqs:SendMessage</c> on the queue, so SNS can
    /// deliver to it, and returns the queue's ARN. Writes the policy only when a topic is missing from it.
    /// </summary>
    public static async Task<string> AllowTopicsAsync(
        IAmazonSQS sqs,
        string queueUrl,
        IEnumerable<string> topicArns,
        CancellationToken cancellationToken
    )
    {
        var (queueArn, policy) = await GetQueueArnAndPolicyAsync(sqs, queueUrl, cancellationToken)
            .ConfigureAwait(false);
        var toAllow = topicArns.Where(arn => !policy.HasSqsPermission(arn, queueArn)).ToList();

        if (toAllow.Count == 0)
        {
            return queueArn;
        }

        policy.AddSqsPermissions(toAllow, queueArn);

        await sqs.SetQueueAttributesAsync(
                queueUrl,
                new Dictionary<string, string>(StringComparer.Ordinal) { ["Policy"] = policy.ToJson() },
                cancellationToken
            )
            .ConfigureAwait(false);

        return queueArn;
    }

    /// <summary>
    /// Subscribes the queue to the topic with raw message delivery, the envelope Bus consumers read, and switches an
    /// existing subscription to it.
    /// </summary>
    public static async Task SubscribeQueueAsync(
        IAmazonSimpleNotificationService sns,
        string topicArn,
        string queueArn,
        CancellationToken cancellationToken
    )
    {
        var request = new SubscribeRequest
        {
            TopicArn = topicArn,
            Protocol = "sqs",
            Endpoint = queueArn,
            // The consumer reads the published body and header bag directly, not SNS's JSON wrapper. Asked for at
            // creation, so no message reaches a new subscription wrapped.
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { [_RawMessageDelivery] = "true" },
        };

        SubscribeResponse response;
        try
        {
            response = await sns.SubscribeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidParameterException)
        {
            // SNS refuses to resubscribe an existing subscription with different attributes; take it as it is and
            // switch it below.
            request.Attributes = null;
            response = await sns.SubscribeAsync(request, cancellationToken).ConfigureAwait(false);
        }

        // Subscribe returns an existing subscription without applying the requested attributes, and a wrapped message
        // would be deleted as malformed, so raw delivery is set on every subscription.
        await sns.SetSubscriptionAttributesAsync(
                response.SubscriptionArn,
                _RawMessageDelivery,
                "true",
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>Lists the ARNs of the topic's SQS subscriptions, keyed by their endpoint (the queue ARN).</summary>
    /// <remarks>One call per 100 subscriptions of the topic.</remarks>
    public static async Task<ILookup<string, string>> ListQueueSubscriptionsAsync(
        IAmazonSimpleNotificationService sns,
        string topicArn,
        CancellationToken cancellationToken
    )
    {
        var subscriptions = new List<Subscription>();
        string? nextToken = null;

        do
        {
            var page = await sns.ListSubscriptionsByTopicAsync(
                    new ListSubscriptionsByTopicRequest { TopicArn = topicArn, NextToken = nextToken },
                    cancellationToken
                )
                .ConfigureAwait(false);

            subscriptions.AddRange(page.Subscriptions ?? []);
            nextToken = page.NextToken;
        } while (!string.IsNullOrEmpty(nextToken));

        return subscriptions
            .Where(static s => string.Equals(s.Protocol, "sqs", StringComparison.OrdinalIgnoreCase))
            .ToLookup(static s => s.Endpoint, static s => s.SubscriptionArn, StringComparer.Ordinal);
    }

    /// <summary>Reads whether the subscription delivers raw messages, as the Bus consumer requires.</summary>
    public static async Task<bool> IsRawDeliveryAsync(
        IAmazonSimpleNotificationService sns,
        string subscriptionArn,
        CancellationToken cancellationToken
    )
    {
        var response = await sns.GetSubscriptionAttributesAsync(subscriptionArn, cancellationToken)
            .ConfigureAwait(false);

        return (response.Attributes ?? []).TryGetValue(_RawMessageDelivery, out var raw)
            && string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The failure of a check that found <paramref name="what"/> missing, naming the deploy step that creates it.</summary>
    public static ResourceProvisioningException Missing(string what, Exception? innerException = null)
    {
        return new ResourceProvisioningException($"Amazon {what} does not exist. {_Fix}", innerException);
    }

    /// <summary>The failure of a check that found each of <paramref name="what"/> missing.</summary>
    public static ResourceProvisioningException Missing(IReadOnlyList<string> what)
    {
        return what.Count == 1
            ? Missing(what[0])
            : new ResourceProvisioningException(
                $"These Amazon objects do not exist: {string.Join("; ", what)}. {_Fix}"
            );
    }

    private const string _Fix =
        "Run 'resources provision' (dotnet <app>.dll resources provision) in the deployment, or call "
        + "AddDeployStepsOnStartup() in development.";
}
