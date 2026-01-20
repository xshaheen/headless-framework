// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Framework.Messages.Messages;

/// <summary>
/// A descriptor of user definition method.
/// </summary>
public class ConsumerExecutorDescriptor
{
    public TypeInfo? ServiceTypeInfo { get; init; }

    public required MethodInfo MethodInfo { get; init; }

    public required TypeInfo ImplTypeInfo { get; init; }

    public List<ParameterDescriptor> Parameters { get; init; } = [];

    public string? TopicNamePrefix { get; init; }

    /// <summary>
    /// Topic name for the consumer. Can be set directly or computed from attributes (legacy).
    /// </summary>
    public required string TopicName { get; init; }

    /// <summary>
    /// Group name for the consumer.
    /// </summary>
    public required string GroupName { get; init; }
}

public sealed class ParameterDescriptor
{
    public required string? Name { get; init; }

    public required Type ParameterType { get; init; }

    public required bool IsFromMessaging { get; init; }
}
