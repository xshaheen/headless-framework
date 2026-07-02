// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

/// <summary>
/// Defines an interface for selecting an consumer service method to invoke for the current message.
/// </summary>
public interface IConsumerServiceSelector
{
    /// <summary>
    /// Selects a set of <see cref="ConsumerExecutorDescriptor" /> candidates for the current message associated with
    /// </summary>
    /// <returns>A set of <see cref="ConsumerExecutorDescriptor" /> candidates or <see langword="null"/>.</returns>
    IReadOnlyList<ConsumerExecutorDescriptor> SelectCandidates();

    /// <summary>
    /// Selects the best <see cref="ConsumerExecutorDescriptor" /> candidate from <paramref name="candidates" /> for the
    /// current message associated.
    /// </summary>
    /// <param name="key">message name or exchange router key.</param>
    /// <param name="candidates">the set of <see cref="ConsumerExecutorDescriptor" /> candidates.</param>
    ConsumerExecutorDescriptor? SelectBestCandidate(string key, IReadOnlyList<ConsumerExecutorDescriptor> candidates);

    /// <summary>
    /// Clears any cached candidate data, forcing re-evaluation on the next selection.
    /// </summary>
    void Invalidate();
}

/// <summary>
/// Creates a new <see cref="ConsumerServiceSelector" />.
/// </summary>
public sealed class ConsumerServiceSelector(IServiceProvider serviceProvider) : IConsumerServiceSelector
{
    /// <summary>
    /// since this class be designed as a Singleton service,the following two list must be thread safe!
    /// </summary>
    private readonly ConcurrentDictionary<
        WildcardCacheKey,
        List<RegexExecuteDescriptor<ConsumerExecutorDescriptor>>
    > _cacheList = new();

    // Per-message-type MethodInfo cache. Each registered consumer message type otherwise pays a
    // MakeGenericType + GetMethod hit on every cache rebuild (Invalidate -> SelectCandidates).
    private static readonly ConcurrentDictionary<Type, MethodInfo> _ConsumeMethodCache = new();

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

        executorDescriptorList.AddRange(_FindConsumersFromControllerTypes());

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
            // Build ConsumerExecutorDescriptor from metadata
            var consumeMethod = _ConsumeMethodCache.GetOrAdd(
                consumer.MessageType,
                static messageType =>
                    typeof(IConsume<>).MakeGenericType(messageType).GetMethod(nameof(IConsume<>.ConsumeAsync))!
            );

            var descriptor = new ConsumerExecutorDescriptor
            {
                ServiceTypeInfo = consumer.ConsumerType.GetTypeInfo(),
                ImplTypeInfo = consumer.ConsumerType.GetTypeInfo(),
                MethodInfo = consumeMethod,
                MessageName = consumer.MessageName,
                GroupName = _GetGroupName(consumer),
                Parameters = _BuildParameters(consumeMethod),
                MessageNamePrefix = _messagingOptions.MessageNamePrefix,
                Concurrency = consumer.Concurrency,
                HandlerId = consumer.ResolvedHandlerId,
                IntentType = consumer.IntentType,
            };

            results.Add(descriptor);
        }

        return results;
    }

    private void _DrainPendingMessageRegistrations()
    {
        SetupMessaging.DrainPendingMessageRegistrations(serviceProvider, _messagingOptions);
    }

    private string _GetGroupName(ConsumerMetadata metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata.Group))
        {
            return metadata.Group;
        }

        _messagingOptions.Conventions.Version = _messagingOptions.Version;
        return _messagingOptions.Conventions.GetGroupName(metadata.ResolvedHandlerId);
    }

    private static List<ParameterDescriptor> _BuildParameters(MethodInfo method)
    {
        return
        [
            .. method
                .GetParameters()
                .Select(p => new ParameterDescriptor
                {
                    Name = p.Name,
                    ParameterType = p.ParameterType,
                    IsFromMessaging = p.ParameterType == typeof(CancellationToken),
                }),
        ];
    }

    private static IEnumerable<ConsumerExecutorDescriptor> _FindConsumersFromControllerTypes()
    {
        // Controller-based consumers are no longer supported with IConsume<T> pattern
        // Use setup.ForMessage<TMessage>(...) instead.
        return [];
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
                    Name = Helper.WildcardToRegex(x.MessageName),
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
        var group = executeDescriptor[0].GroupName;
        var intentType = executeDescriptor[0].IntentType;

        for (var i = 1; i < executeDescriptor.Count; i++)
        {
            if (executeDescriptor[i].IntentType != intentType)
            {
                return new WildcardCacheKey(group, IntentType: null);
            }
        }

        return new WildcardCacheKey(group, intentType);
    }

    private sealed class RegexExecuteDescriptor<T>
    {
        public required string Name { get; init; }

        public required T Descriptor { get; init; }
    }

    private readonly record struct WildcardCacheKey(string GroupName, IntentType? IntentType);
}
