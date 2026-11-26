// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using System.Text.Json.Serialization.Metadata;
using Headless.Checks;
using Headless.Dashboard;
using Headless.DevInbox.Dashboard.Internal;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Headless.DevInbox.Dashboard;

/// <summary>The module's own reads under <c>{base}/api/dev-inbox</c>: its channels, and each channel's list and detail.</summary>
internal sealed class DevInboxEndpoints(DevInboxChannels channels)
{
    internal const int DefaultListLimit = 50;

    internal const int MaxListLimit = 200;

    internal const string UnconfiguredCode = "channel_unconfigured";

    private static readonly (string Messages, string Setup) _EmailSetup = ("emails", "AddHeadlessEmails");

    private static readonly (string Messages, string Setup) _SmsSetup = ("SMS", "AddHeadlessSms");

    private static readonly (string Messages, string Setup) _PushSetup = (
        "push notifications",
        "AddHeadlessPushNotifications"
    );

    private readonly DevInboxChannels _channels = Argument.IsNotNull(channels);

    public void Map(DashboardModuleEndpoints endpoints)
    {
        var routes = endpoints.Routes;

        DevInboxNoStore.Apply(routes.MapGet("/channels", _Channels));

        DevInboxNoStore.Apply(
            routes.MapGet(
                "/emails",
                (string? search, string? before, int? limit) =>
                    _List(
                        _channels.Email,
                        _EmailSetup,
                        search,
                        before,
                        limit,
                        DevInboxJsonContext.Default.DevInboxListDevInboxEmailRow
                    )
            )
        );
        DevInboxNoStore.Apply(
            routes.MapGet(
                "/sms",
                (string? search, string? before, int? limit) =>
                    _List(
                        _channels.Sms,
                        _SmsSetup,
                        search,
                        before,
                        limit,
                        DevInboxJsonContext.Default.DevInboxListDevInboxSmsRow
                    )
            )
        );
        DevInboxNoStore.Apply(
            routes.MapGet(
                "/push",
                (string? search, string? before, int? limit) =>
                    _List(
                        _channels.Push,
                        _PushSetup,
                        search,
                        before,
                        limit,
                        DevInboxJsonContext.Default.DevInboxListDevInboxPushRow
                    )
            )
        );

        DevInboxNoStore.Apply(routes.MapGet("/emails/{id}", (string id) => _Detail(_channels.Email, _EmailSetup, id)));
        DevInboxNoStore.Apply(routes.MapGet("/sms/{id}", (string id) => _Detail(_channels.Sms, _SmsSetup, id)));
        DevInboxNoStore.Apply(routes.MapGet("/push/{id}", (string id) => _Detail(_channels.Push, _PushSetup, id)));
    }

    /// <summary>
    /// The answer for a channel whose capture store the host did not register, naming the provider call it needs.
    /// </summary>
    internal static IResult Unconfigured((string Messages, string Setup) channel) =>
        TypedResults.Problem(
            detail: $"This host captures no {channel.Messages}. Call UseDevelopment() in {channel.Setup} to capture them.",
            statusCode: StatusCodes.Status404NotFound,
            title: "Channel not configured",
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["code"] = UnconfiguredCode }
        );

    /// <summary>Reads a capture id; a malformed id reads as a capture that does not exist, since a stale URL is the usual cause.</summary>
    internal static bool TryParseId(string id, out Guid parsed) => Guid.TryParse(id, out parsed);

    private JsonHttpResult<DevInboxChannelsView> _Channels()
    {
        return TypedResults.Json(
            new DevInboxChannelsView([
                .. _channels.All.Select(static c => new DevInboxChannelView(c.Kind, c.Capacity)),
            ]),
            DevInboxJsonContext.Default.DevInboxChannelsView
        );
    }

    private static IResult _List<TCapture, TRow, TDetail>(
        DevInboxChannel<TCapture, TRow, TDetail>? channel,
        (string Messages, string Setup) channelName,
        string? search,
        string? before,
        int? limit,
        JsonTypeInfo<DevInboxList<TRow>> listType
    )
        where TCapture : class
    {
        if (channel is null)
        {
            return Unconfigured(channelName);
        }

        var pageSize = limit ?? DefaultListLimit;

        if (pageSize is < 1 or > MaxListLimit)
        {
            return DashboardValidation.Problem(
                "limit",
                pageSize < 1 ? DashboardValidation.MinimumRule : DashboardValidation.MaximumRule,
                $"limit must be between 1 and {MaxListLimit}."
            );
        }

        long? cursor = null;

        if (!string.IsNullOrEmpty(before))
        {
            if (!long.TryParse(before, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                return DashboardValidation.Problem(
                    "before",
                    DashboardValidation.FormatRule,
                    "before must be the nextCursor of an earlier page."
                );
            }

            cursor = parsed;
        }

        var (items, next) = channel.List(string.IsNullOrEmpty(search) ? null : search, cursor, pageSize);

        return TypedResults.Json(new DevInboxList<TRow>(items, next?.ToString(CultureInfo.InvariantCulture)), listType);
    }

    private static IResult _Detail<TCapture, TRow, TDetail>(
        DevInboxChannel<TCapture, TRow, TDetail>? channel,
        (string Messages, string Setup) channelName,
        string id
    )
        where TCapture : class
    {
        if (channel is null)
        {
            return Unconfigured(channelName);
        }

        return TryParseId(id, out var parsed) && channel.Get(parsed) is { } capture
            ? TypedResults.Json(channel.ToDetail(capture), channel.DetailType)
            : TypedResults.NotFound();
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
