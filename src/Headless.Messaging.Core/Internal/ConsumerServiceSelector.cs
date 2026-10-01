// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Messaging.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

/// <summary>
/// Default <see cref="IConsumerServiceSelector"/> that discovers <see cref="IConsume{TMessage}"/> registrations
/// and matches incoming message names against them (exact match before wildcard regex).
/// </summary>
internal sealed class ConsumerServiceSelector(IServiceProvider serviceProvider) : IConsumerServiceSelector
{
    /// <summary>
    /// since this class be designed as a Singleton service,the following two list must be thread safe!
    /// </summary>
    private readonly ConcurrentDictionary<
        WildcardCacheKey,
        List<RegexExecuteDescriptor<ConsumerExecutorDescriptor>>
    > _cacheList = new();

    private readonly MessagingOptions _messagingOptions = serviceProvider
        .GetRequiredService<IOptions<MessagingOptions>>()
        .Value;

    private readonly ILogger<ConsumerServiceSelector> _logger = serviceProvider.GetRequiredService<
        ILogger<ConsumerServiceSelector>
    >();

    private readonly IRuntimeConsumerRegistry _runtimeConsumerRegistry =
        serviceProvider.GetService<IRuntimeConsumerRegistry>() ?? EmptyRuntimeConsumerRegistry.Instance;

    public void Invalidate()
    {
        _cacheList.Clear();
    }

    public IReadOnlyList<ConsumerExecutorDescriptor> SelectCandidates()
    {
        var executorDescriptorList = new List<ConsumerExecutorDescriptor>();

        executorDescriptorList.AddRange(_FindConsumersFromInterfaceTypes(serviceProvider));
        executorDescriptorList.AddRange(_runtimeConsumerRegistry.GetDescriptors());

        return executorDescriptorList.Distinct(new ConsumerExecutorDescriptorComparer(_logger)).ToList();
    }

    public ConsumerExecutorDescriptor? SelectBestCandidate(
        string key,
        IReadOnlyList<ConsumerExecutorDescriptor> candidates
    )
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var result = _MatchUsingName(key, candidates);
        if (result != null)
        {
            return result;
        }

        //[*] match with regex, i.e.  foo.*.abc
        //[#] match regex, i.e. foo.#
        return _MatchWildcardUsingRegex(key, candidates);
    }

    private List<ConsumerExecutorDescriptor> _FindConsumersFromInterfaceTypes(IServiceProvider provider)
    {
        // Get registered consumers from the ConsumerRegistry
        var registry = provider.GetService<ConsumerRegistry>();

        // If no registry was registered, there are no consumers to select.
        if (registry == null)
        {
            return [];
        }

        // Defensive fallback: in a hosted app the bootstrapper has already drained synchronously (before any
        // processor started), so this is a no-op. It performs the first drain only in manual/test hosts that
        // bypass the bootstrapper. See DrainPendingMessageRegistrations for the threading invariant it relies on.
        _DrainPendingMessageRegistrations();

        var results = new List<ConsumerExecutorDescriptor>();
        var metadata = registry.GetAll();

        foreach (var consumer in metadata)
        {
            // ConsumeOnly narrows what this host consumes, not what it registers: a filtered-out consumer gets no
            // client here, while its messages stay publishable. Every-instance consumers are exempt because they hold
            // per-process state that every host must keep current.
            if (!registry.ConsumeFilter.Allows(consumer))
            {
                continue;
            }

            var descriptor = new ConsumerExecutorDescriptor
            {
                ConsumerType = consumer.ConsumerType,
                MessageType = consumer.MessageType,
                MessageName = consumer.MessageName,
                SubscriptionName = consumer.SubscriptionName,
                Concurrency = consumer.Concurrency,
                ConsumerIdentity = consumer.ConsumerIdentity,
                MessageContractVersion = consumer.MessageContractVersion,
                InboxRetention = consumer.InboxRetention,
                Lane = consumer.Lane,
                EveryInstance = consumer.EveryInstance,
                Dispatch = consumer.Dispatch,
                Middleware = consumer.Middleware,
            };

            results.Add(descriptor);
        }

        return results;
    }

    private void _DrainPendingMessageRegistrations()
    {
        SetupMessaging.DrainPendingMessageRegistrations(serviceProvider, _messagingOptions);
    }

    private static ConsumerExecutorDescriptor? _MatchUsingName(
        string key,
        IReadOnlyList<ConsumerExecutorDescriptor> executeDescriptor
    )
    {
        Argument.IsNotNull(key);

        // Indexed scan instead of FirstOrDefault(closure): same first-match semantics with no per-message
        // closure + iterator allocation (this runs per transport-dispatched message).
        for (var i = 0; i < executeDescriptor.Count; i++)
        {
            var descriptor = executeDescriptor[i];

            if (descriptor.MessageName.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return descriptor;
            }
        }

        return null;
    }

    private ConsumerExecutorDescriptor? _MatchWildcardUsingRegex(
        string key,
        IReadOnlyList<ConsumerExecutorDescriptor> executeDescriptor
    )
    {
        var cacheKey = _CreateWildcardCacheKey(executeDescriptor);
        if (!_cacheList.TryGetValue(cacheKey, out var tmpList))
        {
            tmpList =
            [
                .. executeDescriptor.Select(x => new RegexExecuteDescriptor<ConsumerExecutorDescriptor>
                {
                    Name = TransportNaming.WildcardToRegex(x.MessageName),
                    Descriptor = x,
                }),
            ];

            _cacheList.TryAdd(cacheKey, tmpList);
        }

        foreach (var red in tmpList)
        {
            if (Regex.IsMatch(key, red.Name, RegexOptions.Singleline, TimeSpan.FromSeconds(1)))
            {
                return red.Descriptor;
            }
        }

        return null;
    }

    private static WildcardCacheKey _CreateWildcardCacheKey(IReadOnlyList<ConsumerExecutorDescriptor> executeDescriptor)
    {
        var subscription = executeDescriptor[0].SubscriptionName;
        var lane = executeDescriptor[0].Lane;

        for (var i = 1; i < executeDescriptor.Count; i++)
        {
            if (executeDescriptor[i].Lane != lane)
            {
                return new WildcardCacheKey(subscription, Lane: null);
            }
        }

        return new WildcardCacheKey(subscription, lane);
    }

    private sealed class RegexExecuteDescriptor<T>
    {
        public required string Name { get; init; }

        public required T Descriptor { get; init; }
    }

    private readonly record struct WildcardCacheKey(string SubscriptionName, MessageLane? Lane);
}
