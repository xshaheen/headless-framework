// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal interface IRuntimeMessageHandlerInvoker
{
    ValueTask InvokeAsync(object consumeContext, IServiceProvider services, CancellationToken cancellationToken);
}

internal interface IRuntimeConsumerRegistry
{
    IReadOnlyList<ConsumerExecutorDescriptor> GetDescriptors();

    RuntimeConsumerRegistrationResult Register<TMessage>(
        RuntimeConsumeHandler<TMessage> handler,
        RuntimeSubscriptionOptions? options = null
    )
        where TMessage : class;

    bool Unregister(string subscriptionId);

    bool TryGetInvoker(
        string topic,
        string group,
        string handlerId,
        [NotNullWhen(true)] out IRuntimeMessageHandlerInvoker? invoker
    );
}

internal sealed class EmptyRuntimeConsumerRegistry : IRuntimeConsumerRegistry
{
    public static EmptyRuntimeConsumerRegistry Instance { get; } = new();

    public IReadOnlyList<ConsumerExecutorDescriptor> GetDescriptors()
    {
        return [];
    }

    public RuntimeConsumerRegistrationResult Register<TMessage>(
        RuntimeConsumeHandler<TMessage> handler,
        RuntimeSubscriptionOptions? options = null
    )
        where TMessage : class
    {
        throw new InvalidOperationException("Runtime consumer registry is not available.");
    }

    public bool Unregister(string subscriptionId)
    {
        return false;
    }

    public bool TryGetInvoker(
        string topic,
        string group,
        string handlerId,
        [NotNullWhen(true)] out IRuntimeMessageHandlerInvoker? invoker
    )
    {
        invoker = null;
        return false;
    }
}

internal enum RuntimeConsumerRegistrationStatus
{
    Attached = 0,
    Ignored = 1,
}

internal sealed record RuntimeConsumerRegistrationResult(
    RuntimeConsumerRegistrationStatus Status,
    string? SubscriptionId,
    string Topic,
    string Group,
    string HandlerId
);

internal sealed record RuntimeConsumerRegistration(
    string SubscriptionId,
    string Topic,
    string Group,
    string HandlerId,
    ConsumerExecutorDescriptor Descriptor,
    IRuntimeMessageHandlerInvoker Invoker
);

internal sealed class RuntimeConsumerRegistry(
    IOptions<MessagingOptions> options,
    ILogger<RuntimeConsumerRegistry> logger
) : IRuntimeConsumerRegistry
{
    private readonly Lock _lock = new();
    private readonly MessagingOptions _options = options.Value;
    private ImmutableArray<RuntimeConsumerRegistration> _registrations = [];

    public IReadOnlyList<ConsumerExecutorDescriptor> GetDescriptors()
    {
        return _registrations.Select(x => x.Descriptor).ToArray();
    }

    public RuntimeConsumerRegistrationResult Register<TMessage>(
        RuntimeConsumeHandler<TMessage> handler,
        RuntimeSubscriptionOptions? options = null
    )
        where TMessage : class
    {
        Argument.IsNotNull(handler);

        var method = handler.Method;
        var handlerId = _ResolveHandlerId(method, typeof(TMessage), options?.HandlerId);
        var topic = _ResolveTopic(typeof(TMessage), options?.Topic);
        var group = _ResolveGroup(handlerId, options?.Group);
        var concurrency = _ResolveConcurrency(options?.Concurrency ?? 1);
        var invoker = new RuntimeMessageHandlerInvoker<TMessage>(handler);
        var descriptor = _CreateDescriptor<TMessage>(method, topic, group, handlerId, concurrency);

        lock (_lock)
        {
            var existing = _registrations.FirstOrDefault(x =>
                string.Equals(x.Topic, topic, StringComparison.Ordinal)
                && string.Equals(x.Group, group, StringComparison.Ordinal)
            );

            if (existing != null)
            {
                switch (options?.DuplicateBehavior ?? RuntimeSubscriptionDuplicateBehavior.Reject)
                {
                    case RuntimeSubscriptionDuplicateBehavior.Ignore:
                        logger.LogInformation(
                            "Ignoring duplicate runtime subscription for topic {Topic}, group {Group}, handler {HandlerId}.",
                            topic,
                            group,
                            handlerId
                        );
                        return new RuntimeConsumerRegistrationResult(
                            RuntimeConsumerRegistrationStatus.Ignored,
                            null,
                            topic,
                            group,
                            existing.HandlerId
                        );
                    case RuntimeSubscriptionDuplicateBehavior.Replace:
                        _registrations = _registrations.Remove(existing);
                        logger.LogWarning(
                            "Replacing runtime subscription for topic {Topic}, group {Group}. Previous handler {ExistingHandlerId}, new handler {HandlerId}.",
                            topic,
                            group,
                            existing.HandlerId,
                            handlerId
                        );
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Duplicate runtime subscription detected for topic/group: "
                                + $"topic='{topic}', group='{group}', existingHandlerId='{existing.HandlerId}', "
                                + $"newHandlerId='{handlerId}'. "
                                + "Set RuntimeSubscriptionOptions.DuplicateBehavior to Ignore or Replace to opt out."
                        );
                }
            }

            var subscriptionId = Guid.NewGuid().ToString("N");
            var registration = new RuntimeConsumerRegistration(
                subscriptionId,
                topic,
                group,
                handlerId,
                descriptor,
                invoker
            );
            _registrations = _registrations.Add(registration);

            return new RuntimeConsumerRegistrationResult(
                RuntimeConsumerRegistrationStatus.Attached,
                subscriptionId,
                topic,
                group,
                handlerId
            );
        }
    }

    public bool Unregister(string subscriptionId)
    {
        Argument.IsNotNullOrWhiteSpace(subscriptionId);

        lock (_lock)
        {
            var existing = _registrations.FirstOrDefault(x =>
                string.Equals(x.SubscriptionId, subscriptionId, StringComparison.Ordinal)
            );
            if (existing == null)
            {
                return false;
            }

            _registrations = _registrations.Remove(existing);
            return true;
        }
    }

    public bool TryGetInvoker(
        string topic,
        string group,
        string handlerId,
        [NotNullWhen(true)] out IRuntimeMessageHandlerInvoker? invoker
    )
    {
        var registration = _registrations.FirstOrDefault(x =>
            string.Equals(x.Topic, topic, StringComparison.Ordinal)
            && string.Equals(x.Group, group, StringComparison.Ordinal)
            && string.Equals(x.HandlerId, handlerId, StringComparison.Ordinal)
        );

        invoker = registration?.Invoker;
        return invoker != null;
    }

    private string _ResolveTopic(Type messageType, string? explicitTopic)
    {
        if (!string.IsNullOrWhiteSpace(explicitTopic))
        {
            return _options.ApplyTopicNamePrefix(explicitTopic!);
        }

        if (_options.TopicMappings.TryGetValue(messageType, out var mappedTopic))
        {
            return _options.ApplyTopicNamePrefix(mappedTopic);
        }

        return _options.ApplyTopicNamePrefix(_options.Conventions.GetTopicName(messageType));
    }

    private string _ResolveGroup(string handlerId, string? explicitGroup)
    {
        _options.Conventions.Version = _options.Version;
        return string.IsNullOrWhiteSpace(explicitGroup) ? _options.Conventions.GetGroupName(handlerId) : explicitGroup!;
    }

    private static byte _ResolveConcurrency(byte concurrency)
    {
        if (concurrency == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(concurrency), "Concurrency must be greater than zero.");
        }

        return concurrency;
    }

    private static string _ResolveHandlerId(
        TypeInfo? declaringType,
        string methodName,
        Type messageType,
        string? explicitHandlerId
    )
    {
        if (!string.IsNullOrWhiteSpace(explicitHandlerId))
        {
            return explicitHandlerId!;
        }

        if (declaringType == null || _RequiresExplicitHandlerId(declaringType.AsType(), methodName))
        {
            throw new InvalidOperationException(
                "Runtime subscriptions require a deterministic handler identity. "
                    + "Use a named method or provide RuntimeSubscriptionOptions.HandlerId explicitly."
            );
        }

        return MessagingConventions.GetDefaultRuntimeHandlerId(declaringType.AsType(), methodName, messageType);
    }

    private static string _ResolveHandlerId(MethodInfo method, Type messageType, string? explicitHandlerId)
    {
        return _ResolveHandlerId(method.DeclaringType?.GetTypeInfo(), method.Name, messageType, explicitHandlerId);
    }

    private static bool _RequiresExplicitHandlerId(Type declaringType, string methodName)
    {
        return declaringType.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false)
            || declaringType.Name.Contains('<', StringComparison.Ordinal)
            || methodName.Contains('<', StringComparison.Ordinal);
    }

    private static ConsumerExecutorDescriptor _CreateDescriptor<TMessage>(
        MethodInfo method,
        string topic,
        string group,
        string handlerId,
        byte concurrency
    )
        where TMessage : class
    {
        var declaringType = method.DeclaringType ?? typeof(RuntimeConsumerRegistry);

        return new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = declaringType.GetTypeInfo(),
            ImplTypeInfo = declaringType.GetTypeInfo(),
            MethodInfo = method,
            TopicName = topic,
            GroupName = group,
            Concurrency = concurrency,
            HandlerId = handlerId,
            Parameters =
            [
                new ParameterDescriptor
                {
                    Name = "context",
                    ParameterType = typeof(ConsumeContext<TMessage>),
                    IsFromMessaging = false,
                },
                new ParameterDescriptor
                {
                    Name = "services",
                    ParameterType = typeof(IServiceProvider),
                    IsFromMessaging = true,
                },
                new ParameterDescriptor
                {
                    Name = "cancellationToken",
                    ParameterType = typeof(CancellationToken),
                    IsFromMessaging = true,
                },
            ],
        };
    }

    private sealed class RuntimeMessageHandlerInvoker<TMessage>(RuntimeConsumeHandler<TMessage> handler)
        : IRuntimeMessageHandlerInvoker
        where TMessage : class
    {
        public ValueTask InvokeAsync(
            object consumeContext,
            IServiceProvider services,
            CancellationToken cancellationToken
        )
        {
            return handler((ConsumeContext<TMessage>)consumeContext, services, cancellationToken);
        }
    }
}
