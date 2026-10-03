// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Reflection;
using System.Runtime.CompilerServices;
using Headless.Checks;
using Headless.Messaging.Configuration;
using Headless.Messaging.Messages;
using Headless.Reliability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Headless.Messaging.Internal;

internal interface IRuntimeMessageHandlerInvoker
{
    ValueTask InvokeAsync(object consumeContext, IServiceProvider services, CancellationToken cancellationToken);
}
