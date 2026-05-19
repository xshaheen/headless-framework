// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Configuration;

internal interface IMiddlewareDescriptorRegistry
{
    IReadOnlyList<MiddlewareDescriptor> Descriptors { get; }

    MiddlewareDescriptor AddOrGet(MiddlewareDescriptorInput input);

    IReadOnlyList<MiddlewareDescriptor> GetPublishDescriptors(Type messageType);

    IReadOnlyList<MiddlewareDescriptor> GetConsumeDescriptors(Type messageType, string? groupName);
}

internal sealed class MiddlewareDescriptorRegistry : IMiddlewareDescriptorRegistry
{
    private readonly Lock _lock = new();
    private readonly List<MiddlewareDescriptor> _descriptors = [];
    private long _nextOrder;

    public IReadOnlyList<MiddlewareDescriptor> Descriptors
    {
        get
        {
            lock (_lock)
            {
                return _descriptors.ToArray();
            }
        }
    }

    public MiddlewareDescriptor AddOrGet(MiddlewareDescriptorInput input)
    {
        lock (_lock)
        {
            var existing = _descriptors.FirstOrDefault(descriptor => descriptor.Matches(input));

            if (existing is not null)
            {
                return existing;
            }

            var descriptor = new MiddlewareDescriptor(
                input.Direction,
                input.Scope,
                input.MiddlewareType,
                input.ServiceType,
                input.ContextType,
                input.MessageType,
                input.GroupName,
                _nextOrder++
            );
            _descriptors.Add(descriptor);

            return descriptor;
        }
    }

    public IReadOnlyList<MiddlewareDescriptor> GetPublishDescriptors(Type messageType)
    {
        lock (_lock)
        {
            var bus = _descriptors
                .Where(descriptor =>
                    descriptor.Direction == MiddlewareDirection.Publish && descriptor.Scope == MiddlewareScope.Bus
                )
                .OrderBy(static descriptor => descriptor.Priority)
                .ThenBy(static descriptor => descriptor.Order);
            var typed = _descriptors
                .Where(descriptor =>
                    descriptor.Direction == MiddlewareDirection.Publish
                    && descriptor.Scope == MiddlewareScope.Message
                    && descriptor.MessageType == messageType
                )
                .OrderBy(static descriptor => descriptor.Priority)
                .ThenBy(static descriptor => descriptor.Order);

            return bus.Concat(typed).ToArray();
        }
    }

    public IReadOnlyList<MiddlewareDescriptor> GetConsumeDescriptors(Type messageType, string? groupName)
    {
        lock (_lock)
        {
            var bus = _descriptors
                .Where(descriptor =>
                    descriptor.Direction == MiddlewareDirection.Consume && descriptor.Scope == MiddlewareScope.Bus
                )
                .OrderBy(static descriptor => descriptor.Priority)
                .ThenBy(static descriptor => descriptor.Order);
            var typed = _descriptors
                .Where(descriptor =>
                    descriptor.Direction == MiddlewareDirection.Consume
                    && descriptor.Scope == MiddlewareScope.Message
                    && descriptor.MessageType == messageType
                    && string.Equals(descriptor.GroupName, groupName, StringComparison.Ordinal)
                )
                .OrderBy(static descriptor => descriptor.Priority)
                .ThenBy(static descriptor => descriptor.Order);

            return bus.Concat(typed).ToArray();
        }
    }
}

internal sealed class MiddlewareDescriptor(
    MiddlewareDirection direction,
    MiddlewareScope scope,
    Type middlewareType,
    Type serviceType,
    Type contextType,
    Type? messageType,
    string? groupName,
    long order
)
{
    public MiddlewareDirection Direction { get; } = direction;

    public MiddlewareScope Scope { get; } = scope;

    public Type MiddlewareType { get; } = middlewareType;

    public Type ServiceType { get; } = serviceType;

    public Type ContextType { get; } = contextType;

    public Type? MessageType { get; } = messageType;

    public string? GroupName { get; } = groupName;

    public long Order { get; } = order;

    public int Priority { get; private set; }

    public void SetPriority(int priority)
    {
        Priority = priority;
    }

    public bool Matches(MiddlewareDescriptorInput input)
    {
        return Direction == input.Direction
            && Scope == input.Scope
            && MiddlewareType == input.MiddlewareType
            && ServiceType == input.ServiceType
            && ContextType == input.ContextType
            && MessageType == input.MessageType
            && string.Equals(GroupName, input.GroupName, StringComparison.Ordinal);
    }
}

internal readonly record struct MiddlewareDescriptorInput(
    MiddlewareDirection Direction,
    MiddlewareScope Scope,
    Type MiddlewareType,
    Type ServiceType,
    Type ContextType,
    Type? MessageType,
    string? GroupName
);

internal enum MiddlewareDirection
{
    Publish,
    Consume,
}

internal enum MiddlewareScope
{
    Bus,
    Message,
}
