// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;

namespace Headless.Messaging;

internal interface IMiddlewareDescriptorRegistry
{
    IReadOnlyList<MiddlewareDescriptor> Descriptors { get; }

    MiddlewareDescriptor AddOrGet(MiddlewareDescriptorInput input);

    /// <summary>
    /// Ends registration. After this, lookups are cached per (message type, lane) and
    /// <see cref="AddOrGet" /> throws for any descriptor not already registered.
    /// </summary>
    void Freeze();

    bool TryGetPublishDescriptors(
        Type messageType,
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    );

    bool TryGetConsumeDescriptors(
        Type messageType,
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    );

    bool TryGetReceiveDescriptors(
        Type messageType,
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    );
}

internal sealed class MiddlewareDescriptorRegistry : IMiddlewareDescriptorRegistry
{
    private readonly Lock _lock = new();
    private readonly List<MiddlewareDescriptor> _descriptors = [];
    private readonly ConcurrentDictionary<(MiddlewareDirection, Type, MessageLane), LookupResult> _lookupCache = new();
    private long _nextOrder;

    // Written under _lock, read lock-free on the per-message path.
    private volatile bool _frozen;

    private readonly record struct LookupResult(bool Found, IReadOnlyList<MiddlewareDescriptor> Descriptors);

    public void Freeze()
    {
        lock (_lock)
        {
            _frozen = true;
        }
    }

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

            // The per-message lookups are cached once frozen, so a descriptor added now would never run.
            if (_frozen)
            {
                throw new InvalidOperationException(
                    $"Cannot register {input.Direction} middleware '{input.MiddlewareType.Name}' after the messaging host has started. "
                        + "Register all middleware while configuring services."
                );
            }

            var descriptor = new MiddlewareDescriptor(
                input.Direction,
                input.Scope,
                input.MiddlewareType,
                input.ServiceType,
                input.ContextType,
                input.MessageType,
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
    ) => _Lookup(MiddlewareDirection.Publish, messageType, lane, out descriptors);

    private bool _Lookup(
        MiddlewareDirection direction,
        Type messageType,
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    )
    {
        if (!_frozen)
        {
            return _Compute(direction, messageType, lane, out descriptors);
        }

        var result = _lookupCache.GetOrAdd(
            (direction, messageType, lane),
            static (key, self) =>
            {
                var found = self._Compute(key.Item1, key.Item2, key.Item3, out var computed);

                return new LookupResult(found, computed);
            },
            this
        );
        descriptors = result.Descriptors;

        return result.Found;
    }

    private bool _Compute(
        MiddlewareDirection direction,
        Type messageType,
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    ) =>
        direction switch
        {
            MiddlewareDirection.Publish => _ComputePublish(messageType, lane, out descriptors),
            MiddlewareDirection.Consume => _ComputeConsume(messageType, lane, out descriptors),
            _ => _ComputeReceive(messageType, lane, out descriptors),
        };

    private bool _ComputePublish(
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
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    ) => _Lookup(MiddlewareDirection.Consume, messageType, lane, out descriptors);

    private bool _ComputeConsume(
        Type messageType,
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
                else if (descriptor.Scope == MiddlewareScope.Message && descriptor.MessageType == messageType)
                {
                    typed.Add(descriptor);
                }
            }

            descriptors = _Sort(bus).Concat(_Sort(typed)).ToArray();

            return hasConsumeDescriptors;
        }
    }

    public bool TryGetReceiveDescriptors(
        Type messageType,
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    ) => _Lookup(MiddlewareDirection.Receive, messageType, lane, out descriptors);

    private bool _ComputeReceive(
        Type messageType,
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    )
    {
        lock (_lock)
        {
            var bus = new List<MiddlewareDescriptor>();
            var typed = new List<MiddlewareDescriptor>();

            foreach (var descriptor in _descriptors)
            {
                if (descriptor.Direction != MiddlewareDirection.Receive)
                {
                    continue;
                }

                if (descriptor.Scope == MiddlewareScope.Bus)
                {
                    // Global receive middleware is lane-agnostic: one registration covers both bus
                    // and queue deliveries, so the lane filter is skipped for bus-scope descriptors.
                    bus.Add(descriptor);
                }
                else if (
                    descriptor.Scope == MiddlewareScope.Message
                    && descriptor.Lane == lane
                    && descriptor.MessageType == messageType
                )
                {
                    typed.Add(descriptor);
                }
            }

            descriptors = _Sort(bus).Concat(_Sort(typed)).ToArray();

            // Unlike the consume lookup, resolution here is registry-only with no DI-scan fallback,
            // so the boolean must mean "middleware will run" — never a lane-matching descriptor that
            // does not participate in this delivery.
            return descriptors.Count > 0;
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
            && Lane == input.Lane;
    }
}

internal readonly record struct MiddlewareDescriptorInput(
    MiddlewareDirection Direction,
    MiddlewareScope Scope,
    Type MiddlewareType,
    Type ServiceType,
    Type ContextType,
    Type? MessageType,
    MessageLane Lane
);

internal enum MiddlewareDirection
{
    Publish,
    Consume,
    Receive,
}

internal enum MiddlewareScope
{
    Bus,
    Message,
}
