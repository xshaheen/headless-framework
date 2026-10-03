// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections;
using System.Diagnostics;
using Headless.Abstractions;
using Headless.MultiTenancy;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Headless.Api.ServiceDefaults;

/// <summary>
/// Tags every span that starts while a tenant is ambient, such as database and outgoing HTTP spans. Spans that start
/// before their tenant is known (the request, consume, and job spans) are tagged where the tenant is resolved.
/// </summary>
internal sealed class TenantActivityProcessor(ICurrentTenant currentTenant, TenantTelemetryOptions options)
    : BaseProcessor<Activity>
{
    public override void OnStart(Activity data)
    {
        if (currentTenant.Id is { } tenantId)
        {
            TenantTelemetry.TagActivity(data, options, tenantId);
        }
    }
}
