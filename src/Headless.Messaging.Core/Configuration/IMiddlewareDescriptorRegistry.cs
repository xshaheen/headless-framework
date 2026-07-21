// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Messaging.Configuration;

internal interface IMiddlewareDescriptorRegistry
{
    IReadOnlyList<MiddlewareDescriptor> Descriptors { get; }

    MiddlewareDescriptor AddOrGet(MiddlewareDescriptorInput input);

    bool TryGetPublishDescriptors(
        Type messageType,
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    );

    bool TryGetConsumeDescriptors(
        Type messageType,
        string? groupName,
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    );
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
                input.Lane,
                _nextOrder++
            );
            _descriptors.Add(descriptor);

            return descriptor;
        }
    }

    public bool TryGetPublishDescriptors(
        Type messageType,
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    )
    {
        lock (_lock)
        {
            var hasPublishDescriptors = false;
            var bus = new List<MiddlewareDescriptor>();
            var typed = new List<MiddlewareDescriptor>();

            foreach (var descriptor in _descriptors)
            {
                if (descriptor.Direction != MiddlewareDirection.Publish)
                {
                    continue;
                }

                if (descriptor.Lane != lane)
                {
                    continue;
                }

                hasPublishDescriptors = true;

                if (descriptor.Scope == MiddlewareScope.Bus)
                {
                    bus.Add(descriptor);
                }
                else if (descriptor.Scope == MiddlewareScope.Message && descriptor.MessageType == messageType)
                {
                    typed.Add(descriptor);
                }
            }

            descriptors = _Sort(bus).Concat(_Sort(typed)).ToArray();

            return hasPublishDescriptors;
        }
    }

    public bool TryGetConsumeDescriptors(
        Type messageType,
        string? groupName,
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    )
    {
        lock (_lock)
        {
            var hasConsumeDescriptors = false;
            var bus = new List<MiddlewareDescriptor>();
            var typed = new List<MiddlewareDescriptor>();

            foreach (var descriptor in _descriptors)
            {
                if (descriptor.Direction != MiddlewareDirection.Consume)
                {
                    continue;
                }

                if (descriptor.Lane != lane)
                {
                    continue;
                }

                hasConsumeDescriptors = true;

                if (descriptor.Scope == MiddlewareScope.Bus)
                {
                    bus.Add(descriptor);
                }
                else if (
                    descriptor.Scope == MiddlewareScope.Message
                    && descriptor.MessageType == messageType
                    && string.Equals(descriptor.GroupName, groupName, StringComparison.Ordinal)
                )
                {
                    typed.Add(descriptor);
                }
            }

            descriptors = _Sort(bus).Concat(_Sort(typed)).ToArray();

            return hasConsumeDescriptors;
        }
    }

    private static IOrderedEnumerable<MiddlewareDescriptor> _Sort(List<MiddlewareDescriptor> descriptors)
    {
        return descriptors
            .OrderBy(static descriptor => descriptor.Priority)
            .ThenBy(static descriptor => descriptor.Order);
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
    MessageLane lane,
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

    public MessageLane Lane { get; } = lane;

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
            && Lane == input.Lane
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
    string? GroupName,
    MessageLane Lane
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
