// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Headless.Dashboard.Authentication;
using Headless.Messaging.Messages;
using Headless.Messaging.Monitoring;
using Microsoft.AspNetCore.Http;

namespace Headless.Messaging.Dashboard;

internal enum OperatorAuthorityStatus
{
    Success,
    Unauthenticated,
    PlaceholderActor,
}
