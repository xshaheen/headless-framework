// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics;
using System.Net;
using System.Text.Json.Serialization;
using Headless.Dashboard.Authentication;
using Headless.Messaging.Configuration;
using Headless.Messaging.Dashboard.GatewayProxy;
using Headless.Messaging.Dashboard.NodeDiscovery;
using Headless.Messaging.Internal;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Headless.Messaging.Persistence;
using Headless.Messaging.Runtime;
using Headless.Messaging.Transport;
using Headless.Primitives;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Dashboard;

internal sealed class WarpResult
{
    public int ChildCount => Values.Count;

    public required string ConsumerIdentity { get; set; }

    public required List<SubInfo> Values { get; set; }

    internal sealed class SubInfo
    {
        public required string MessageName { get; set; }

        public required string Lane { get; set; }

        public required string ImplName { get; set; }

        public required string MethodEscaped { get; set; }
    }
}
