// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Reflection;
using Headless.Checks;
using Headless.DistributedLocks;
using Headless.Messaging.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Headless.Messaging.Configuration;

/// <summary>
/// A marker service registered when the host opts in to sending requests. Bootstrap reads it to reject a transport
/// without a reply channel before the host reports ready; a host without it opens no reply listener.
/// </summary>
internal sealed class RequestReplyMarkerService;
