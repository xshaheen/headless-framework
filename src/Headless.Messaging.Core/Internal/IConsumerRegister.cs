// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Headless.Messaging.CircuitBreaker;
using Headless.Messaging.Configuration;
using Headless.Messaging.Diagnostics;
using Headless.Messaging.Exceptions;
using Headless.Messaging.Messages;
using Headless.Messaging.Persistence;
using Headless.Messaging.RequestReply;
using Headless.Messaging.Retry;
using Headless.Messaging.Runtime;
using Headless.Messaging.Serialization;
using Headless.Messaging.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

/// <summary>
/// Handler received message of subscribed.
/// </summary>
internal interface IConsumerRegister : IProcessingServer
{
    bool IsHealthy();

    ValueTask ReStartAsync(bool force = false, CancellationToken cancellationToken = default);
    ValueTask OnTopologyChangedAsync(CancellationToken cancellationToken = default);
}
