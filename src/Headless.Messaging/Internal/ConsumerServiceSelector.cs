// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Headless.Checks;
using Headless.Reliability;
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
    /// Compiled wildcard patterns keyed by message name. The selector is a singleton, so the cache must be thread
    /// safe. It is keyed by the pattern itself, not by the candidate list: callers hand in lists grouped by
    /// subscription, by subscription and lane, and by consumer identity, and no key derived from a list's first
    /// element tells two different lists apart.
    /// </summary>
    private readonly ConcurrentDictionary<string, Regex> _patternCache = new(StringComparer.Ordinal);

    private readonly ILogger<ConsumerServiceSelector> _logger = serviceProvider.GetRequiredService<
        ILogger<ConsumerServiceSelector>
    >();

    private readonly IRuntimeConsumerRegistry _runtimeConsumerRegistry =
        serviceProvider.GetService<IRuntimeConsumerRegistry>() ?? EmptyRuntimeConsumerRegistry.Instance;

    public void Invalidate()
    {
        _patternCache.Clear();
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

    private static List<ConsumerExecutorDescriptor> _FindConsumersFromInterfaceTypes(IServiceProvider provider)
    {
        // Get registered consumers from the ConsumerRegistry
        var registry = provider.GetService<ConsumerRegistry>();

        // If no registry was registered, there are no consumers to select.
        if (registry == null)
        {
            return [];
        }

        var ownership = provider.GetRequiredService<IConsumerHostOwnership>();
        var results = new List<ConsumerExecutorDescriptor>();
        var metadata = registry.GetAll();

        foreach (var consumer in metadata)
        {
            // ConsumeOnly narrows what this host consumes, not what it registers: a filtered-out consumer gets no
            // client here, while its messages stay publishable.
            if (!ownership.Starts(consumer))
            {
                continue;
            }

            var descriptor = new ConsumerExecutorDescriptor
            {
                ConsumerType = consumer.ConsumerType,
                MethodName = consumer.IsResponder ? nameof(IRespond<,>.RespondAsync) : nameof(IConsume<>.ConsumeAsync),
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
                OnSubscriptionEstablished = consumer.OnSubscriptionEstablished,
                ResponseType = consumer.ResponseType,
                Middleware = consumer.Middleware,
                RateLimit = consumer.RateLimit,
                // The registry resolves a policy for every competing consumer; an every-instance one never retries.
                FailurePolicy = consumer.FailurePolicy ?? FailurePolicyDefinition.None,
            };

            results.Add(descriptor);
        }

        return results;
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
        for (var i = 0; i < executeDescriptor.Count; i++)
        {
            var descriptor = executeDescriptor[i];

            // Built once per pattern: the static Regex.IsMatch cache holds only a few patterns, so more wildcard
            // subscriptions than that would re-parse their patterns on every delivery.
            var pattern = _patternCache.GetOrAdd(
                descriptor.MessageName,
                static name => new Regex(
                    TransportNaming.WildcardToRegex(name),
                    RegexOptions.Singleline,
                    TimeSpan.FromSeconds(1)
                )
            );

            if (pattern.IsMatch(key))
            {
                return descriptor;
            }
        }

        return null;
    }
}
