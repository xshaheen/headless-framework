// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Configuration;
using Headless.Messaging.Registration;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal interface IMessageMetadataRegistry
{
    IReadOnlyCollection<MessageMetadata> GetAll();

    bool TryGet(MessageRouteKey route, [NotNullWhen(true)] out MessageMetadata? metadata);
}
