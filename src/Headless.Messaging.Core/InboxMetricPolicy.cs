// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Headless.Messaging.Configuration;
using Headless.Messaging.Internal;
using Headless.Messaging.Monitoring;

namespace Headless.Messaging;

/// <summary>How inbox measurements treat the tenant dimension, resolved once from the host configuration.</summary>
/// <param name="TenantTagName">The tenant attribute to add to inbox measurements, or <see langword="null"/> to omit it.</param>
internal sealed record InboxMetricPolicy(string? TenantTagName);
