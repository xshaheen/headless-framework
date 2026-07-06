// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;

namespace Headless.Messaging.Messages;

/// <summary>
/// A descriptor of user definition method.
/// </summary>
public sealed class ConsumerExecutorDescriptor
{
    public TypeInfo? ServiceTypeInfo { get; init; }

    public required MethodInfo MethodInfo { get; init; }

    public required TypeInfo ImplTypeInfo { get; init; }

    public IReadOnlyList<ParameterDescriptor> Parameters { get; init; } = [];

    public string? MessageNamePrefix { get; init; }

    /// <summary>
    /// Message name for the consumer. Can be set directly or computed from attributes.
    /// </summary>
    public required string MessageName { get; init; }

    /// <summary>
    /// Group name for the consumer.
    /// </summary>
    public required string GroupName { get; init; }

    /// <summary>
    /// Maximum number of messages to process concurrently for this consumer.
    /// </summary>
    public byte Concurrency { get; init; } = 1;

    /// <summary>
    /// Deterministic handler identity used for diagnostics and runtime subscription matching.
    /// </summary>
    public string? HandlerId { get; init; }

    /// <summary>
    /// Delivery intent used to subscribe this consumer.
    /// </summary>
    public required IntentType IntentType { get; init; }

    /// <summary>
    /// The message payload type used for deserialization: <c>T</c> when the first non-framework parameter is
    /// <see cref="ConsumeContext{T}"/>, otherwise that parameter's type. Cached — descriptors are immutable
    /// after registration, so recomputing this per received message is pure reflection overhead. The benign
    /// publication race writes the same <see cref="Type"/> reference.
    /// </summary>
    public Type? MessageValueType => field ??= _ComputeMessageValueType();

    /// <summary>
    /// The <c>T</c> of the consumer's <see cref="ConsumeContext{T}"/> parameter, or <see langword="null"/> when
    /// the method has no such parameter. Cached for the same reason as <see cref="MessageValueType"/>.
    /// </summary>
    public Type? ConsumeContextValueType => field ??= _ComputeConsumeContextValueType();

    private Type? _ComputeMessageValueType()
    {
        foreach (var parameter in Parameters)
        {
            if (parameter.IsFromMessaging)
            {
                continue;
            }

            var parameterType = parameter.ParameterType;

            return parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(ConsumeContext<>)
                ? parameterType.GetGenericArguments()[0]
                : parameterType;
        }

        return null;
    }

    private Type? _ComputeConsumeContextValueType()
    {
        foreach (var parameter in Parameters)
        {
            var parameterType = parameter.ParameterType;

            if (parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(ConsumeContext<>))
            {
                return parameterType.GetGenericArguments()[0];
            }
        }

        return null;
    }
}

public sealed class ParameterDescriptor
{
    public required string? Name { get; init; }

    public required Type ParameterType { get; init; }

    public required bool IsFromMessaging { get; init; }
}
