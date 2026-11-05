// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Headless.Dashboard;

/// <summary>
/// <c>GET api/payload?module=&amp;kind=&amp;id=</c>: one object's payload, read from its module and redacted by the
/// module's formatter before it is sent, because redaction in the browser is not redaction.
/// </summary>
internal static class PayloadEndpoints
{
    // The shell's SPA reads these field names; the host's own JSON options must not rename them.
    private static readonly JsonSerializerOptions _JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Maps the endpoint on <paramref name="dashboard"/>, the route group at the dashboard's base path.</summary>
    internal static void Map(IEndpointRouteBuilder dashboard)
    {
        // Typed as a result-returning handler: a lambda over HttpContext alone binds as a RequestDelegate, which
        // would discard the IResult and send an empty 200.
        Func<HttpContext, Task<IResult>> handler = static context => _GetAsync(context, context.RequestAborted);

        dashboard.MapGet("/api/payload", handler).RequireDashboardShellAccess(moduleId: null).ExcludeFromDescription();
    }

    private static async Task<IResult> _GetAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var query = context.Request.Query;
        var moduleId = query["module"].ToString();
        var kind = query["kind"].ToString();
        var id = query["id"].ToString();

        if (moduleId.Length == 0 || kind.Length == 0 || id.Length == 0)
        {
            return Results.BadRequest();
        }

        var services = context.RequestServices;
        var manifest = services.GetRequiredService<DashboardManifestProvider>().Manifest;
        var owner = manifest.Modules.FirstOrDefault(m => string.Equals(m.Id, moduleId, StringComparison.Ordinal));

        if (owner is not { IsRefused: false, Module: IDashboardPayloadSource source })
        {
            return Results.NotFound();
        }

        var user = context.User;

        var access = await services
            .GetRequiredService<DashboardPermissions>()
            .GetSessionSnapshotAsync(context)
            .ConfigureAwait(false);

        if (!access.MayView(owner.Id))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var target = new ObjectRef
        {
            Module = moduleId,
            Kind = kind,
            Id = id,
        };

        var limit = services.GetRequiredService<IOptions<HeadlessDashboardOptions>>().Value.AggregationTimeout;
        using var timeout = new CancellationTokenSource(limit, services.GetRequiredService<TimeProvider>());
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        DashboardPayload? raw;

        try
        {
            // WaitAsync bounds a module that ignores its token, as the other aggregation calls do.
            raw = await source
                .GetPayloadAsync(new DashboardPayloadRequest { Target = target, User = user }, linked.Token)
                .WaitAsync(linked.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
        }

        if (raw is null)
        {
            return Results.NotFound();
        }

        var payload = await services
            .GetRequiredService<DashboardPayloadFormatting>()
            .FormatAsync(target, raw, user, cancellationToken)
            .ConfigureAwait(false);

        return Results.Json(new PayloadResponse(payload.Content, payload.ContentType, payload.Headers), _JsonOptions);
    }

    private sealed record PayloadResponse(
        string Content,
        string ContentType,
        IReadOnlyDictionary<string, string> Headers
    );
}
