// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The endpoints another node calls at <c>{base}/api/messaging/peer</c>: this node's own Needs you, id lookup,
/// payloads, command availability, change feed, sidebar counts, Overview, and the scope and chunks of an Operation
/// over this node's rows.
/// </summary>
/// <remarks>
/// <para>
/// Each answers only a call the forwarding scheme admitted, and is <c>404</c> to anything else, so a signed-in user
/// cannot reach the node-to-node surface. Every answer is this node's own: a peer call is never forwarded again.
/// </para>
/// <para>
/// An Operation chunk is recorded in this node's operator log under the operator the calling node named, as
/// <c>&lt;operator&gt; via &lt;calling node&gt;</c>, beside the calling node's own record of the Operation.
/// </para>
/// </remarks>
internal static class PeerEndpoints
{
    public static void Map(RouteGroupBuilder peer, MessagingModule module)
    {
        peer.MapGet(
            "/attention",
            Task<IResult> (HttpContext context) =>
                _AnswerAsync(
                    context,
                    async token => await module.LocalSources.GetAttentionAsync(token).ConfigureAwait(false)
                )
        );
        peer.MapGet(
            "/resolve",
            (HttpContext context, string? q) =>
                _AnswerAsync(
                    context,
                    async token =>
                        await module.LocalSources.ResolveAsync(q ?? string.Empty, token).ConfigureAwait(false)
                )
        );
        peer.MapGet(
            "/payload",
            (HttpContext context, string? kind, string? id) => _PayloadAsync(context, module, kind, id)
        );
        peer.MapGet(
            "/allowed",
            (HttpContext context, string? kind, string[]? id) => _AllowedAsync(context, module, kind, id)
        );
        peer.MapGet(
            "/changes/start",
            Task<IResult> (HttpContext context) =>
                _AnswerAsync(
                    context,
                    async token => new PeerCursor(
                        await module.LocalChanges.GetStartCursorAsync(token).ConfigureAwait(false)
                    )
                )
        );
        peer.MapGet("/changes", (HttpContext context, string? cursor) => _ChangesAsync(context, module, cursor));
        peer.MapGet(
            "/counts",
            Task<IResult> (HttpContext context) =>
                _AnswerAsync(
                    context,
                    async token => await module.LocalCounts.GetCountsAsync(token).ConfigureAwait(false)
                )
        );
        peer.MapGet(
            "/overview",
            Task<IResult> (HttpContext context) =>
                _AnswerAsync(
                    context,
                    async token => await module.LocalSources.GetOverviewAsync(token).ConfigureAwait(false)
                )
        );
#pragma warning disable ASP0018 // False positive: the handlers read commandId from the route values, not a bound parameter.
        peer.MapPost(
            "/operations/{commandId}/scope",
            Task<IResult> (HttpContext context) => _ScopeAsync(context, module)
        );
        peer.MapPost(
            "/operations/{commandId}/chunk",
            Task<IResult> (HttpContext context) => _ChunkAsync(context, module)
        );
#pragma warning restore ASP0018
        peer.ExcludeFromDescription();
    }

    private static bool _IsPeerCall(HttpContext context) =>
        DashboardPeerAdmission.IsAdmitted(context, MessagingDashboardOptions.ModuleId)
        && MessagingForwarding.IsForwarded(context.User);

    private static async Task<IResult> _AnswerAsync<T>(HttpContext context, Func<CancellationToken, Task<T>> read)
    {
        if (!_IsPeerCall(context))
        {
            return Results.NotFound();
        }

        return Results.Json(await read(context.RequestAborted).ConfigureAwait(false), MessagingForwarding.JsonOptions);
    }

    private static async Task<IResult> _PayloadAsync(
        HttpContext context,
        MessagingModule module,
        string? kind,
        string? id
    )
    {
        if (!_IsPeerCall(context))
        {
            return Results.NotFound();
        }

        if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(id))
        {
            return Results.Problem(
                detail: "Send the kind and the id of the message.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        var payload = await module
            .LocalSources.GetPayloadAsync(MessagingViews.Ref(kind, id), context.RequestAborted)
            .ConfigureAwait(false);

        return payload is null ? Results.NotFound() : Results.Json(payload, MessagingForwarding.JsonOptions);
    }

    private static async Task<IResult> _AllowedAsync(
        HttpContext context,
        MessagingModule module,
        string? kind,
        string[]? id
    )
    {
        if (!_IsPeerCall(context))
        {
            return Results.NotFound();
        }

        if (string.IsNullOrEmpty(kind) || id is not { Length: > 0 })
        {
            return Results.Problem(
                detail: "Send the kind and at least one id.",
                statusCode: StatusCodes.Status400BadRequest
            );
        }

        // Through the module, so this node's own ceiling for forwarded calls decides what is offered.
        var answers = await module
            .GetAllowedCommandsAsync(
                new DashboardAllowedCommandsRequest
                {
                    Targets = [.. id.Select(value => MessagingViews.Ref(kind, value))],
                    User = context.User,
                },
                context.RequestAborted
            )
            .ConfigureAwait(false);

        return Results.Json(answers, MessagingForwarding.JsonOptions);
    }

    private static async Task<IResult> _ChangesAsync(HttpContext context, MessagingModule module, string? cursor)
    {
        if (!_IsPeerCall(context))
        {
            return Results.NotFound();
        }

        if (string.IsNullOrEmpty(cursor))
        {
            return Results.Problem(detail: "Send the change cursor.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var batch = await module
                .LocalChanges.ReadChangesAsync(cursor, context.RequestAborted)
                .ConfigureAwait(false);

            return Results.Json(batch, MessagingForwarding.JsonOptions);
        }
        catch (ArgumentException)
        {
            // A cursor this node does not know, such as one from before it restarted: the caller starts over.
            return Results.Problem(detail: "The change cursor is unknown here.", statusCode: StatusCodes.Status410Gone);
        }
    }

    private static async Task<IResult> _ScopeAsync(HttpContext context, MessagingModule module)
    {
        if (!_IsPeerCall(context))
        {
            return Results.NotFound();
        }

        if (_FindCommand(context) is not { } command)
        {
            return Results.NotFound();
        }

        var body = await _ReadAsync<PeerScopeBody>(context).ConfigureAwait(false);

        if (body is null)
        {
            return Results.Problem(detail: "Send the Operation's filter.", statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var scope = await module
                .LocalCommands.PrepareAsync(
                    new DashboardOperationScopeRequest
                    {
                        CommandId = command.Id,
                        Filter = body.Filter,
                        CountLimit = body.CountLimit,
                        SampleSize = body.SampleSize,
                        User = context.User,
                    },
                    context.RequestAborted
                )
                .ConfigureAwait(false);

            return Results.Json(scope, MessagingForwarding.JsonOptions);
        }
        catch (ArgumentException exception)
        {
            return Results.Problem(detail: _Message(exception), statusCode: StatusCodes.Status400BadRequest);
        }
    }

    private static async Task<IResult> _ChunkAsync(HttpContext context, MessagingModule module)
    {
        if (!_IsPeerCall(context))
        {
            return Results.NotFound();
        }

        if (_FindCommand(context) is not { } command)
        {
            return Results.NotFound();
        }

        // The forwarding policy already refuses a write at view; this keeps the rule beside the write it guards.
        if (MessagingForwarding.IsViewOnly(context.User))
        {
            return Results.Problem(
                detail: PeerStates.ViewOnly(module.NodeName),
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        var body = await _ReadAsync<PeerChunkBody>(context).ConfigureAwait(false);

        if (body is null)
        {
            return Results.Problem(detail: "Send the Operation's chunk.", statusCode: StatusCodes.Status400BadRequest);
        }

        var entry = new OperatorLogEntry
        {
            Timestamp = CommandEndpoints.Now(context),
            Actor = CommandAuthorization.GetActor(context.User),
            Channel = CommandEndpoints.GetChannel(context),
            Module = MessagingDashboardOptions.ModuleId,
            CommandId = command.Id,
            Action = OperatorLogAction.OperationChunk,
            Scope = body.Filter.GetRawText(),
            Count = body.ChunkSize,
            Reason = body.Reason,
            Cursor = body.Cursor,
        };

        DashboardOperationChunk chunk;

        try
        {
            chunk = await module
                .LocalCommands.RunChunkAsync(
                    new DashboardOperationChunkRequest
                    {
                        CommandId = command.Id,
                        Filter = body.Filter,
                        HighWaterMark = body.HighWaterMark,
                        Cursor = body.Cursor,
                        ChunkSize = body.ChunkSize,
                        Arguments = body.Arguments ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal),
                        Reason = body.Reason,
                        User = context.User,
                    },
                    context.RequestAborted
                )
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            // Checked before any row changes, so, like every refused call, it writes no log entry.
            return Results.Problem(detail: _Message(exception), statusCode: StatusCodes.Status400BadRequest);
        }
        catch
        {
            // Some rows of the chunk may have changed before it failed; the attempt is recorded before the error
            // reaches the host's error handling.
            await CommandEndpoints.WriteLogAsync(context, entry with { Failed = body.ChunkSize }).ConfigureAwait(false);
            throw;
        }

        var (done, skipped, failed) = CommandEndpoints.Tally(chunk.Rows);

        await CommandEndpoints
            .WriteLogAsync(
                context,
                entry with
                {
                    Timestamp = CommandEndpoints.Now(context),
                    Ids = [.. chunk.Rows.Select(static row => row.Id)],
                    Count = chunk.Rows.Count,
                    Done = done,
                    Skipped = skipped,
                    Failed = failed,
                }
            )
            .ConfigureAwait(false);

        return Results.Json(chunk, MessagingForwarding.JsonOptions);
    }

    // Only a command this host has not disabled runs here, whichever node asks for it.
    private static DashboardCommand? _FindCommand(HttpContext context)
    {
        var id = context.Request.RouteValues["commandId"] as string;
        var module = context
            .RequestServices.GetRequiredService<DashboardManifestProvider>()
            .Manifest.Modules.FirstOrDefault(static m =>
                string.Equals(m.Id, MessagingDashboardOptions.ModuleId, StringComparison.Ordinal)
            );

        return module?.ServerCommands.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
    }

    private static async Task<T?> _ReadAsync<T>(HttpContext context)
        where T : class
    {
        try
        {
            return await JsonSerializer
                .DeserializeAsync<T>(context.Request.Body, MessagingForwarding.JsonOptions, context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string _Message(ArgumentException exception)
    {
        var suffix = $" (Parameter '{exception.ParamName}')";

        return exception.ParamName is not null && exception.Message.EndsWith(suffix, StringComparison.Ordinal)
            ? exception.Message[..^suffix.Length]
            : exception.Message;
    }
}
