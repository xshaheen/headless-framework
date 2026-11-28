// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;

namespace Headless.Messaging.AzureServiceBus;

/// <summary>
/// The administration calls that create Azure Service Bus entities, shared by <c>resources provision</c>, a runtime
/// subscription's client, and an every-instance subscription's client, so each creates an entity with the same settings.
/// </summary>
/// <remarks>
/// Every create tolerates the entity appearing between the check and the create, so two deploy steps, or a deploy step
/// and a runtime subscription, can run at once.
/// </remarks>
internal static class AzureServiceBusTopology
{
    /// <summary>The fix a missing entity names, matching every other transport topology.</summary>
    public const string ProvisionHint =
        "Run 'resources provision' (dotnet <app>.dll resources provision) in the deployment, or call "
        + "AddDeployStepsOnStartup() in development.";

    /// <summary>
    /// The rules a subscription that consumes <paramref name="messageNames"/> holds: one correlation rule per message
    /// name and one rule per configured SQL filter, in a stable order.
    /// </summary>
    public static List<string> RuleNames(AzureServiceBusMessagingOptions options, IEnumerable<string> messageNames) =>
        [.. messageNames.Concat(options.SqlFilters.Select(static o => o.Key)).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// The filter of the rule named <paramref name="ruleName"/>: the configured SQL filter of that name, otherwise a
    /// correlation filter on the message name with the configured default correlation headers.
    /// </summary>
    public static RuleFilter CreateRuleFilter(AzureServiceBusMessagingOptions options, string ruleName)
    {
        var sqlExpression = options
            .SqlFilters.FirstOrDefault(o => string.Equals(o.Key, ruleName, StringComparison.Ordinal))
            .Value;

        if (sqlExpression is not null)
        {
            return new SqlRuleFilter(sqlExpression);
        }

        var correlationRule = new CorrelationRuleFilter { Subject = ruleName };

        foreach (var correlationHeader in options.DefaultCorrelationHeaders)
        {
            correlationRule.ApplicationProperties.Add(correlationHeader.Key, correlationHeader.Value);
        }

        return correlationRule;
    }

    /// <summary>The settings of a subscription the transport creates, idle deletion aside.</summary>
    public static CreateSubscriptionOptions SubscriptionOptions(
        AzureServiceBusMessagingOptions options,
        string topicPath,
        string subscriptionName,
        TimeSpan autoDeleteOnIdle
    ) =>
        new(topicPath, subscriptionName)
        {
            RequiresSession = options.EnableSessions,
            AutoDeleteOnIdle = autoDeleteOnIdle,
            LockDuration = options.SubscriptionMessageLockDuration,
            DefaultMessageTimeToLive = options.SubscriptionDefaultMessageTimeToLive,
            MaxDeliveryCount = options.SubscriptionMaxDeliveryCount,
        };

    /// <summary>The settings of a Queue-lane queue the transport creates.</summary>
    public static CreateQueueOptions QueueOptions(AzureServiceBusMessagingOptions options, string queueName) =>
        new(queueName)
        {
            RequiresSession = options.EnableSessions,
            AutoDeleteOnIdle = options.SubscriptionAutoDeleteOnIdle,
            LockDuration = options.SubscriptionMessageLockDuration,
            DefaultMessageTimeToLive = options.SubscriptionDefaultMessageTimeToLive,
            MaxDeliveryCount = options.SubscriptionMaxDeliveryCount,
        };

    /// <summary>Creates the topic unless it exists.</summary>
    /// <returns>Whether this call created it.</returns>
    public static async Task<bool> EnsureTopicAsync(
        ServiceBusAdministrationClient administration,
        string topicPath,
        ILogger? logger,
        CancellationToken cancellationToken
    )
    {
        if (await administration.TopicExistsAsync(topicPath, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        if (
            !await _CreateUnlessExistsAsync(() => administration.CreateTopicAsync(topicPath, cancellationToken))
                .ConfigureAwait(false)
        )
        {
            return false;
        }

        logger?.TopicCreated(topicPath);

        return true;
    }

    /// <summary>
    /// Creates the subscription unless it exists. Given <paramref name="firstRule"/>, the subscription is created with
    /// that rule instead of the default match-all rule, so it never holds a message its consumer does not handle.
    /// </summary>
    /// <returns>Whether this call created it.</returns>
    public static async Task<bool> EnsureSubscriptionAsync(
        ServiceBusAdministrationClient administration,
        AzureServiceBusMessagingOptions options,
        CreateSubscriptionOptions subscription,
        string? firstRule,
        ILogger? logger,
        CancellationToken cancellationToken
    )
    {
        if (
            await administration
                .SubscriptionExistsAsync(subscription.TopicName, subscription.SubscriptionName, cancellationToken)
                .ConfigureAwait(false)
        )
        {
            return false;
        }

        var created = await _CreateUnlessExistsAsync(() =>
                firstRule is null
                    ? administration.CreateSubscriptionAsync(subscription, cancellationToken)
                    : administration.CreateSubscriptionAsync(
                        subscription,
                        new CreateRuleOptions { Name = firstRule, Filter = CreateRuleFilter(options, firstRule) },
                        cancellationToken
                    )
            )
            .ConfigureAwait(false);

        if (created)
        {
            logger?.SubscriptionCreated(subscription.TopicName, subscription.SubscriptionName);
        }

        return created;
    }

    /// <summary>
    /// Makes the subscription's rules exactly <paramref name="ruleNames"/>: adds the missing ones, deletes the rest (the
    /// default match-all rule included), and rewrites a rule whose filter no longer matches the options.
    /// </summary>
    public static async Task SyncRulesAsync(
        ServiceBusAdministrationClient administration,
        AzureServiceBusMessagingOptions options,
        string topicPath,
        string subscriptionName,
        IReadOnlyCollection<string> ruleNames,
        ILogger? logger,
        CancellationToken cancellationToken
    )
    {
        var existing = await GetRulesAsync(administration, topicPath, subscriptionName, cancellationToken)
            .ConfigureAwait(false);
        var existingNames = existing.ConvertAll(static rule => rule.Name);

        foreach (var newRule in ruleNames.Except(existingNames, StringComparer.Ordinal))
        {
            await _CreateUnlessExistsAsync(() =>
                    administration.CreateRuleAsync(
                        topicPath,
                        subscriptionName,
                        new CreateRuleOptions { Name = newRule, Filter = CreateRuleFilter(options, newRule) },
                        cancellationToken
                    )
                )
                .ConfigureAwait(false);

            logger?.RuleAdded(newRule);
        }

        foreach (var rule in existing)
        {
            if (!ruleNames.Contains(rule.Name, StringComparer.Ordinal))
            {
                await administration
                    .DeleteRuleAsync(topicPath, subscriptionName, rule.Name, cancellationToken)
                    .ConfigureAwait(false);

                logger?.RuleRemoved(rule.Name);

                continue;
            }

            // A changed SQL expression or default correlation header must reach the broker, or the rule keeps
            // filtering by the old one.
            var expected = CreateRuleFilter(options, rule.Name);
            if (!expected.Equals(rule.Filter))
            {
                rule.Filter = expected;
                await administration
                    .UpdateRuleAsync(topicPath, subscriptionName, rule, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    /// <summary>Reads every rule of the subscription.</summary>
    /// <exception cref="ServiceBusException">The topic or the subscription does not exist.</exception>
    public static async Task<List<RuleProperties>> GetRulesAsync(
        ServiceBusAdministrationClient administration,
        string topicPath,
        string subscriptionName,
        CancellationToken cancellationToken
    )
    {
        var rules = new List<RuleProperties>();

        await foreach (
#pragma warning disable MA0079 // False positive: the pageable already carries the cancellation token.
            var rule in administration
                .GetRulesAsync(topicPath, subscriptionName, cancellationToken)
                .ConfigureAwait(false)
#pragma warning restore MA0079
        )
        {
            rules.Add(rule);
        }

        return rules;
    }

    /// <summary>Creates the queue unless it exists.</summary>
    /// <returns>Whether this call created it.</returns>
    public static async Task<bool> EnsureQueueAsync(
        ServiceBusAdministrationClient administration,
        CreateQueueOptions queue,
        CancellationToken cancellationToken
    )
    {
        if (await administration.QueueExistsAsync(queue.Name, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        return await _CreateUnlessExistsAsync(() => administration.CreateQueueAsync(queue, cancellationToken))
            .ConfigureAwait(false);
    }

    /// <summary>Whether <paramref name="exception"/> says the entity the call named does not exist.</summary>
    public static bool IsNotFound(Exception exception) =>
        exception is ServiceBusException { Reason: ServiceBusFailureReason.MessagingEntityNotFound };

    private static async Task<bool> _CreateUnlessExistsAsync<T>(Func<Task<T>> create)
    {
        try
        {
            await create().ConfigureAwait(false);

            return true;
        }
        catch (ServiceBusException e) when (e.Reason is ServiceBusFailureReason.MessagingEntityAlreadyExists)
        {
            // Another deploy step, or a runtime subscription of another process, created it first.
            return false;
        }
    }
}

internal static partial class AzureServiceBusTopologyLog
{
    [LoggerMessage(EventId = 3008, Level = LogLevel.Information, Message = "Azure Service Bus add rule: {NewRule}")]
    internal static partial void RuleAdded(this ILogger logger, string newRule);

    [LoggerMessage(EventId = 3009, Level = LogLevel.Information, Message = "Azure Service Bus remove rule: {OldRule}")]
    internal static partial void RuleRemoved(this ILogger logger, string oldRule);

    [LoggerMessage(
        EventId = 3010,
        Level = LogLevel.Information,
        Message = "Azure Service Bus created topic: {TopicPath}"
    )]
    internal static partial void TopicCreated(this ILogger logger, string topicPath);

    [LoggerMessage(
        EventId = 3011,
        Level = LogLevel.Information,
        Message = "Azure Service Bus topic {TopicPath} created subscription: {SubscriptionName}"
    )]
    internal static partial void SubscriptionCreated(this ILogger logger, string topicPath, string subscriptionName);
}
