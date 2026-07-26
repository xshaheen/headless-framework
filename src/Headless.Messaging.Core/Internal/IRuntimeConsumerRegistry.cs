// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
        string messageName,
        string group,
        string handlerId,
        MessageLane lane,
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
        string messageName,
        string group,
        string handlerId,
        MessageLane lane,
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
    string MessageName,
    string Group,
    string HandlerId,
    MessageLane Lane
);

internal sealed record RuntimeConsumerRegistration(
    string SubscriptionId,
    string MessageName,
    string Group,
    string HandlerId,
    MessageLane Lane,
    ConsumerExecutorDescriptor Descriptor,
    IRuntimeMessageHandlerInvoker Invoker
);

internal sealed class RuntimeConsumerRegistry(
    IOptions<MessagingOptions> options,
    IConsumerRegistry consumerRegistry,
    ILogger<RuntimeConsumerRegistry> logger
) : IRuntimeConsumerRegistry
{
    private readonly Lock _lock = new();
    private readonly MessagingOptions _options = options.Value;

    // Lock-free snapshot reads are intentional: writers replace the array under _lock with a
    // new immutable instance, and un-synchronized readers (GetDescriptors, TryGetInvoker) treat
    // the field as an atomically-assigned reference snapshot. ImmutableArray<T> wraps a single
    // T[] reference so the assignment is atomic; visibility is eventually consistent.
    private ImmutableArray<RuntimeConsumerRegistration> _registrations = [];

    public IReadOnlyList<ConsumerExecutorDescriptor> GetDescriptors()
    {
        // ReSharper disable once InconsistentlySynchronizedField
        return _registrations.Select(x => x.Descriptor).ToArray();
    }

    public RuntimeConsumerRegistrationResult Register<TMessage>(
        RuntimeConsumeHandler<TMessage> handler,
        RuntimeSubscriptionOptions? options = null
    )
        where TMessage : class
    {
        Argument.IsNotNull(handler);

        const MessageLane lane = MessageLane.Bus;
        var method = handler.Method;
        var handlerId = _ResolveHandlerId(method, typeof(TMessage), options?.HandlerId);
        var messageName = _ResolveMessageName(typeof(TMessage), lane, options?.MessageName);
        var group = _ResolveGroup(handlerId, options?.Group);
        var concurrency = Argument.IsPositive(options?.Concurrency ?? 1);
        var invoker = new RuntimeMessageHandlerInvoker<TMessage>(handler);
        var descriptor = _CreateDescriptor<TMessage>(method, messageName, group, handlerId, concurrency, lane);

        lock (_lock)
        {
            var existing = _registrations.FirstOrDefault(x =>
                x.Lane == lane
                && string.Equals(x.MessageName, messageName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Group, group, StringComparison.Ordinal)
            );

            if (existing != null)
            {
                switch (options?.DuplicateBehavior ?? RuntimeSubscriptionDuplicateBehavior.Reject)
                {
                    case RuntimeSubscriptionDuplicateBehavior.Ignore:
                        logger.DuplicateRuntimeSubscriptionIgnored(messageName, group, handlerId);
                        return new RuntimeConsumerRegistrationResult(
                            Status: RuntimeConsumerRegistrationStatus.Ignored,
                            SubscriptionId: null,
                            MessageName: messageName,
                            Group: group,
                            HandlerId: existing.HandlerId,
                            Lane: lane
                        );
                    case RuntimeSubscriptionDuplicateBehavior.Replace:
                        _registrations = _registrations.Remove(existing);
                        logger.DuplicateRuntimeSubscriptionReplaced(messageName, group, existing.HandlerId, handlerId);
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Duplicate runtime subscription detected for messageName/group: "
                                + $"messageName='{messageName}', group='{group}', existingHandlerId='{existing.HandlerId}', "
                                + $"newHandlerId='{handlerId}'. "
                                + "Set RuntimeSubscriptionOptions.DuplicateBehavior to Ignore or Replace to opt out."
                        );
                }
            }

            var subscriptionId = Guid.NewGuid().ToString("N");
            var registration = new RuntimeConsumerRegistration(
                subscriptionId,
                messageName,
                group,
                handlerId,
                lane,
                descriptor,
                invoker
            );
            _registrations = _registrations.Add(registration);

            return new RuntimeConsumerRegistrationResult(
                RuntimeConsumerRegistrationStatus.Attached,
                subscriptionId,
                messageName,
                group,
                handlerId,
                lane
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
        string messageName,
        string group,
        string handlerId,
        MessageLane lane,
        [NotNullWhen(true)] out IRuntimeMessageHandlerInvoker? invoker
    )
    {
        // ReSharper disable once InconsistentlySynchronizedField
        var registration = _registrations.FirstOrDefault(x =>
            x.Lane == lane
            && string.Equals(x.MessageName, messageName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.Group, group, StringComparison.Ordinal)
            && string.Equals(x.HandlerId, handlerId, StringComparison.Ordinal)
        );

        invoker = registration?.Invoker;
        return invoker != null;
    }

    private string _ResolveMessageName(Type messageType, MessageLane lane, string? explicitMessageName)
    {
        if (!string.IsNullOrWhiteSpace(explicitMessageName))
        {
            return _options.ApplyMessageNamePrefix(explicitMessageName);
        }

        if (consumerRegistry.TryGetRawMessageName(messageType, lane, out var mappedMessageName))
        {
            return _options.ApplyMessageNamePrefix(mappedMessageName);
        }

        return _options.ApplyMessageNamePrefix(_options.Conventions.GetMessageName(messageType));
    }

    private string _ResolveGroup(string handlerId, string? explicitGroup)
    {
        return _options.ResolveGroupName(handlerId, explicitGroup);
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
            return explicitHandlerId;
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
        string messageName,
        string group,
        string handlerId,
        byte concurrency,
        MessageLane lane
    )
        where TMessage : class
    {
        var declaringType = method.DeclaringType ?? typeof(RuntimeConsumerRegistry);

        return new ConsumerExecutorDescriptor
        {
            ServiceTypeInfo = declaringType.GetTypeInfo(),
            ImplTypeInfo = declaringType.GetTypeInfo(),
            MethodInfo = method,
            MessageName = messageName,
            GroupName = group,
            Concurrency = concurrency,
            HandlerId = handlerId,
            IntentType = MessageLaneCompatibility.ToIntentType(lane),
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

internal static partial class RuntimeConsumerRegistryLog
{
    [LoggerMessage(
        EventId = 3102,
        Level = LogLevel.Information,
        Message = "Ignoring duplicate runtime subscription for messageName {MessageName}, group {Group}, handler {HandlerId}."
    )]
    public static partial void DuplicateRuntimeSubscriptionIgnored(
        this ILogger logger,
        string messageName,
        string group,
        string handlerId
    );

    [LoggerMessage(
        EventId = 3103,
        Level = LogLevel.Warning,
        Message = "Replacing runtime subscription for messageName {MessageName}, group {Group}. Previous handler {ExistingHandlerId}, new handler {HandlerId}."
    )]
    public static partial void DuplicateRuntimeSubscriptionReplaced(
        this ILogger logger,
        string messageName,
        string group,
        string existingHandlerId,
        string handlerId
    );
}
