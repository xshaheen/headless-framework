// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Framework.Messages;

/// <inheritdoc />
/// <summary>
/// An abstract attribute that for kafka attribute or rabbit mq attribute
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public abstract class TopicAttribute(string name, bool isPartial = false) : Attribute
{
    /// <summary>
    /// Topic or exchange route key name.
    /// </summary>
    public string Name { get; } = name;

    /// <summary>
    /// Defines whether this attribute defines a topic subscription partial.
    /// The defined topic will be combined with a topic subscription defined on class level,
    /// which results for example in subscription on "class.method".
    /// </summary>
    public bool IsPartial { get; } = isPartial;

    /// <summary>
    /// Default group name is CapOptions setting.(Assembly name)
    /// Kafka --> groups.id
    /// RabbitMQ --> queue.name
    /// </summary>
    public string Group { get; set; } = null!;

    /// <summary>
    /// Limit the number of messages consumed concurrently.
    /// If you set this value but don't specify the Group, we will automatically create a Group using the Name.
    /// </summary>
    public byte GroupConcurrent { get; set; }
}

/// <summary>
/// Represents an attribute that is applied to a method to indicate that it is a subscriber for a specific CAP topic.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="CapSubscribeAttribute"/> class with the specified topic name and partial flag.
/// </remarks>
/// <param name="name">The name of the CAP topic.</param>
/// <param name="isPartial">A flag indicating whether the subscriber is a partial subscriber.</param>
public sealed class CapSubscribeAttribute(string name, bool isPartial = false) : TopicAttribute(name, isPartial)
{
    /// <summary>
    /// Returns a string that represents the current <see cref="CapSubscribeAttribute"/>.
    /// </summary>
    /// <returns>A string that represents the current <see cref="CapSubscribeAttribute"/>.</returns>
    public override string ToString() => Name;
}

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromCapAttribute : Attribute;
