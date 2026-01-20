// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Framework.Checks;
using Framework.Messages.Configuration;
using Framework.Messages.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Framework.Messages.Internal;

/// <inheritdoc />
/// <summary>
/// A default <see cref="T:IConsumerServiceSelector" /> implementation.
/// </summary>
public class ConsumerServiceSelector : IConsumerServiceSelector
{
    /// <summary>
    /// since this class be designed as a Singleton service,the following two list must be thread safe!
    /// </summary>
    private readonly ConcurrentDictionary<string, List<RegexExecuteDescriptor<ConsumerExecutorDescriptor>>> _cacheList;

    private readonly MessagingOptions _messagingOptions;
    private readonly ILogger<ConsumerServiceSelector> _logger;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Creates a new <see cref="ConsumerServiceSelector" />.
    /// </summary>
    public ConsumerServiceSelector(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _messagingOptions = serviceProvider.GetRequiredService<IOptions<MessagingOptions>>().Value;
        _logger = serviceProvider.GetRequiredService<ILogger<ConsumerServiceSelector>>();
        _cacheList = new ConcurrentDictionary<string, List<RegexExecuteDescriptor<ConsumerExecutorDescriptor>>>(
            StringComparer.Ordinal
        );
    }

    public IReadOnlyList<ConsumerExecutorDescriptor> SelectCandidates()
    {
        var executorDescriptorList = new List<ConsumerExecutorDescriptor>();

        executorDescriptorList.AddRange(FindConsumersFromInterfaceTypes(_serviceProvider));

        executorDescriptorList.AddRange(FindConsumersFromControllerTypes());

        executorDescriptorList = executorDescriptorList
            .Distinct(new ConsumerExecutorDescriptorComparer(_logger))
            .ToList();

        return executorDescriptorList;
    }

    public ConsumerExecutorDescriptor? SelectBestCandidate(
        string key,
        IReadOnlyList<ConsumerExecutorDescriptor> executeDescriptor
    )
    {
        if (executeDescriptor.Count == 0)
        {
            return null;
        }

        var result = _MatchUsingName(key, executeDescriptor);
        if (result != null)
        {
            return result;
        }

        //[*] match with regex, i.e.  foo.*.abc
        //[#] match regex, i.e. foo.#
        return _MatchWildcardUsingRegex(key, executeDescriptor);
    }

    protected virtual IEnumerable<ConsumerExecutorDescriptor> FindConsumersFromInterfaceTypes(IServiceProvider provider)
    {
        // Get registered consumers from the ConsumerRegistry
        var registry = provider.GetService<ConsumerRegistry>();

        // If no registry found, return empty (backwards compatibility with old pattern)
        if (registry == null)
        {
            return [];
        }

        var results = new List<ConsumerExecutorDescriptor>();
        var metadata = registry.GetAll();

        foreach (var consumer in metadata)
        {
            // Build ConsumerExecutorDescriptor from metadata
            var consumeMethod = typeof(IConsume<>)
                .MakeGenericType(consumer.MessageType)
                .GetMethod(nameof(IConsume<object>.Consume))!;

            var descriptor = new ConsumerExecutorDescriptor
            {
                ServiceTypeInfo = consumer.ConsumerType.GetTypeInfo(),
                ImplTypeInfo = consumer.ConsumerType.GetTypeInfo(),
                MethodInfo = consumeMethod,
                TopicName = consumer.Topic,
                GroupName = _GetGroupName(consumer),
                Parameters = _BuildParameters(consumeMethod),
                TopicNamePrefix = _messagingOptions.TopicNamePrefix,
            };

            results.Add(descriptor);
        }

        return results;
    }

    private string _GetGroupName(ConsumerMetadata metadata)
    {
        var prefix = !string.IsNullOrEmpty(_messagingOptions.GroupNamePrefix)
            ? $"{_messagingOptions.GroupNamePrefix}."
            : string.Empty;

        var baseGroup = metadata.Group ?? _messagingOptions.DefaultGroupName;

        return $"{prefix}{baseGroup}.{_messagingOptions.Version}";
    }

    private List<ParameterDescriptor> _BuildParameters(MethodInfo method)
    {
        return method
            .GetParameters()
            .Select(p => new ParameterDescriptor
            {
                Name = p.Name!,
                ParameterType = p.ParameterType,
                IsFromMessaging = p.ParameterType == typeof(CancellationToken),
            })
            .ToList();
    }

    protected virtual IEnumerable<ConsumerExecutorDescriptor> FindConsumersFromControllerTypes()
    {
        // Controller-based consumers are no longer supported with IConsume<T> pattern
        // Use IMessagingBuilder.ScanConsumers() or Consumer<T>() instead
        return [];
    }

    private static ConsumerExecutorDescriptor? _MatchUsingName(
        string key,
        IReadOnlyList<ConsumerExecutorDescriptor> executeDescriptor
    )
    {
        Argument.IsNotNull(key);

        return executeDescriptor.FirstOrDefault(x => x.TopicName.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    private ConsumerExecutorDescriptor? _MatchWildcardUsingRegex(
        string key,
        IReadOnlyList<ConsumerExecutorDescriptor> executeDescriptor
    )
    {
        var group = executeDescriptor[0].GroupName;
        if (!_cacheList.TryGetValue(group, out var tmpList))
        {
            tmpList = executeDescriptor
                .Select(x => new RegexExecuteDescriptor<ConsumerExecutorDescriptor>
                {
                    Name = Helper.WildcardToRegex(x.TopicName),
                    Descriptor = x,
                })
                .ToList();
            _cacheList.TryAdd(group, tmpList);
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

    private class RegexExecuteDescriptor<T>
    {
        public string Name { get; set; } = default!;

        public T Descriptor { get; set; } = default!;
    }
}
