// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;
using Headless.Dashboard;
using Headless.DevInbox.Dashboard.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Headless.DevInbox.Dashboard;

/// <summary>The module's own reads under <c>{base}/api/dev-inbox</c>.</summary>
internal sealed class DevInboxEndpoints(DevInboxStores stores)
{
    private readonly DevInboxStores _stores = Argument.IsNotNull(stores);

    public void Map(DashboardModuleEndpoints endpoints)
    {
        var routes = endpoints.Routes;

        DevInboxNoStore.Apply(routes.MapGet("/channels", _Channels));
    }

    private JsonHttpResult<DevInboxChannelsView> _Channels()
    {
        List<DevInboxChannelView> channels = [];

        if (_stores.Email is { } email)
        {
            channels.Add(new DevInboxChannelView("email", email.Capacity));
        }

        if (_stores.Sms is { } sms)
        {
            channels.Add(new DevInboxChannelView("sms", sms.Capacity));
        }

        if (_stores.Push is { } push)
        {
            channels.Add(new DevInboxChannelView("push", push.Capacity));
        }

        return TypedResults.Json(new DevInboxChannelsView(channels), DevInboxJsonContext.Default.DevInboxChannelsView);
    }
}

internal static class DevInboxNoStore
{
    /// <summary>
    /// Forbids every cache from keeping the response: captured messages hold reset links and one-time codes that must
    /// not outlive their deletion in a browser or proxy cache.
    /// </summary>
    public static RouteHandlerBuilder Apply(RouteHandlerBuilder builder)
    {
        return builder.AddEndpointFilter(
            static async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";

                return await next(context).ConfigureAwait(false);
            }
        );
    }
}
