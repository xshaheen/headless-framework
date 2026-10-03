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
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    );

    bool TryGetReceiveDescriptors(
        Type messageType,
        MessageLane lane,
        out IReadOnlyList<MiddlewareDescriptor> descriptors
    );
}
