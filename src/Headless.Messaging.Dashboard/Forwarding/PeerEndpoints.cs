// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Text.Json;
using Headless.Dashboard;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Messaging.Dashboard;

/// <summary>
/// The endpoints another node calls at <c>{base}/api/messaging/peer</c>: this node's own Needs you, id lookup,
/// correlation trace, payloads, command availability, change feed, sidebar counts, Overview, the sequential intake locks it holds, a
/// ping, and the scope and chunks of an Operation over this node's rows.
/// </summary>
/// <remarks>
/// <para>
/// Each answers only a call the forwarding scheme admitted, and is <c>404</c> to anything else, so a signed-in user
/// cannot reach the node-to-node surface. Every answer is this node's own: a peer call is never forwarded again.
/// </para>
/// <para>
/// An Operation over this node's rows is held to the limits and checks of a local one, whatever the calling node
/// already checked: the scope step refuses a filter matching more than <see cref="CommandRules.MaxRows"/> rows and
/// signs the scope it counted into the mark it answers, and each chunk runs only over a scope this node signed, at the
/// shell's chunk size, with its reason and arguments checked against the command. A refusal answers the error a local
/// confirm or chunk answers.
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
        // A POST only so the looked-up text and the correlation id stay out of the URL; both read, so a view-only node
        // admits them.
        peer.MapPost("/resolve", Task<IResult> (HttpContext context) => _ResolveAsync(context, module))
            .WithMetadata(MessagingReadEndpoint.Instance);
        peer.MapPost("/trace", Task<IResult> (HttpContext context) => _TraceAsync(context, module))
            .WithMetadata(MessagingReadEndpoint.Instance);
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
                        await module.PeerChanges.GetStartCursorAsync(token).ConfigureAwait(false)
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
        peer.MapGet(
            "/sequential",
            (HttpContext context, string? resource, string? consumer) =>
                _IntakeHoldAsync(context, module, resource, consumer)
        );
        // A ping proves this node admits the calling node's signed call and answers; it reads nothing.
        peer.MapGet(
            "/ping",
            Task<IResult> (HttpContext context) =>
                _AnswerAsync(context, _ => Task.FromResult(new PeerPing(module.NodeName)))
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
            return DashboardValidation.Problem(
                string.IsNullOrEmpty(kind) ? "kind" : "id",
                DashboardValidation.RequiredRule,
                "Send the kind and the id of the message."
            );
        }

        var payload = await module
            .LocalSources.GetPayloadAsync(MessagingViews.Ref(kind, id), context.RequestAborted)
            .ConfigureAwait(false);

        return payload is null ? Results.NotFound() : Results.Json(payload, MessagingForwarding.JsonOptions);
    }

    private static async Task<IResult> _ResolveAsync(HttpContext context, MessagingModule module)
    {
        if (!_IsPeerCall(context))
        {
            return Results.NotFound();
        }

        var body = await _ReadBodyAsync<PeerResolveBody>(context).ConfigureAwait(false);
        var matches = await module
            .LocalSources.ResolveAsync(body?.Query ?? string.Empty, context.RequestAborted)
            .ConfigureAwait(false);

        return Results.Json(matches, MessagingForwarding.JsonOptions);
    }

    private static async Task<IResult> _TraceAsync(HttpContext context, MessagingModule module)
    {
        if (!_IsPeerCall(context))
        {
            return Results.NotFound();
        }

        var body = await _ReadBodyAsync<PeerTraceBody>(context).ConfigureAwait(false);

        // The messages name the fields, never the id: a validation problem is served and may be logged.
        if (
            body?.CorrelationId
            is not { Length: > 0 and <= MessagingSearchRequestValidator.MaxCorrelationIdLength } correlationId
        )
        {
            return DashboardValidation.Problem(
                "correlationId",
                string.IsNullOrEmpty(body?.CorrelationId)
                    ? DashboardValidation.RequiredRule
                    : DashboardValidation.MaxLengthRule,
                $"Send a correlation id of at most {MessagingSearchRequestValidator.MaxCorrelationIdLength} characters."
            );
        }

        if (body.Limit is not { } limit || limit is < 1 or > MessagingDashboardOptionsValidator.MaxTraceLimit)
        {
            return DashboardValidation.Problem(
                "limit",
                DashboardValidation.InvalidRule,
                $"Send a limit between 1 and {MessagingDashboardOptionsValidator.MaxTraceLimit}."
            );
        }

        var part = await module
            .LocalSources.GetTraceAsync(correlationId, limit, context.RequestAborted)
            .ConfigureAwait(false);

        return Results.Json(part, MessagingForwarding.JsonOptions);
    }

    // A malformed body reads as no body, so the caller gets the field-named problem instead of a 500.
    private static async Task<T?> _ReadBodyAsync<T>(HttpContext context)
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

    private static Task<IResult> _IntakeHoldAsync(
        HttpContext context,
        MessagingModule module,
        string? resource,
        string? consumer
    )
    {
        if (!_IsPeerCall(context))
        {
            return Task.FromResult(Results.NotFound());
        }

        if (string.IsNullOrEmpty(resource) || string.IsNullOrEmpty(consumer))
        {
            return Task.FromResult<IResult>(
                DashboardValidation.Problem(
                    string.IsNullOrEmpty(resource) ? "resource" : "consumer",
                    DashboardValidation.RequiredRule,
                    "Send the lock resource and the consumer identity."
                )
            );
        }

        return Task.FromResult(
            Results.Json(module.SequentialStates.ReadLocal(resource, consumer), MessagingForwarding.JsonOptions)
        );
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
            return DashboardValidation.Problem(
                string.IsNullOrEmpty(kind) ? "kind" : "id",
                DashboardValidation.RequiredRule,
                "Send the kind and at least one id."
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
            return DashboardValidation.Problem("cursor", DashboardValidation.RequiredRule, "Send the change cursor.");
        }

        try
        {
            var batch = await module.PeerChanges.ReadChangesAsync(cursor, context.RequestAborted).ConfigureAwait(false);

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

        var (body, bodyErrors) = await CommandCallBody
            .ReadAsync(context.Request, context.RequestAborted)
            .ConfigureAwait(false);

        if (body is null)
        {
            return _Invalid(bodyErrors);
        }

        if (body.Filter is not { } filter)
        {
            return _Invalid([OperationEndpoints.FilterRequired()]);
        }

        DashboardOperationScope scope;

        try
        {
            // This node's own limits, not the calling node's: a peer call is held to what a local confirm is.
            scope = await module
                .LocalCommands.PrepareAsync(
                    new DashboardOperationScopeRequest
                    {
                        CommandId = command.Id,
                        Filter = filter,
                        CountLimit = CommandRules.MaxRows,
                        SampleSize = CommandRules.SampleSize,
                        User = context.User,
                    },
                    context.RequestAborted
                )
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            return _Invalid([OperationEndpoints.FilterRefused(exception)]);
        }

        if (scope.Count > CommandRules.MaxRows)
        {
            return _Invalid([OperationEndpoints.ScopeTooLarge()]);
        }

        // The scope this node checked is signed into the mark the calling node sends back on every chunk, so a chunk
        // runs only over a scope this node froze and counted.
        var token = OperationEndpoints.NewToken(context, MessagingDashboardOptions.ModuleId, command, filter, scope);
        var protector = context.RequestServices.GetRequiredService<PeerOperationScopeProtector>();

        return Results.Json(
            scope with
            {
                HighWaterMark = protector.Tokens.Protect(token),
            },
            MessagingForwarding.JsonOptions
        );
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

        var (body, bodyErrors) = await CommandCallBody
            .ReadAsync(context.Request, context.RequestAborted)
            .ConfigureAwait(false);

        if (body is null)
        {
            return _Invalid(bodyErrors);
        }

        if (string.IsNullOrEmpty(body.Token))
        {
            return _Invalid([new CommandFieldError("token", "required", "Send the mark the scope step returned.")]);
        }

        var protector = context.RequestServices.GetRequiredService<PeerOperationScopeProtector>();

        if (!protector.Tokens.TryUnprotect(body.Token, out var token, out var tokenError))
        {
            return _Invalid([tokenError]);
        }

        if (OperationEndpoints.TokenScopeError(token, MessagingDashboardOptions.ModuleId, command.Id) is { } wrongScope)
        {
            return _Invalid([wrongScope]);
        }

        // A not-before-now argument is checked against the moment the scope was frozen, as a local Operation checks it
        // once at confirm, so a long Operation does not start refusing its own arguments halfway through.
        var (arguments, errors) = OperationEndpoints.ValidateCall(context, command, body, token.FrozenAt);

        if (errors.Count > 0)
        {
            return _Invalid(errors);
        }

        var reason = CommandEndpoints.Reason(body);
        var entry = new OperatorLogEntry
        {
            Timestamp = CommandEndpoints.Now(context),
            Actor = CommandAuthorization.GetActor(context.User),
            Channel = CommandEndpoints.GetChannel(context),
            Module = MessagingDashboardOptions.ModuleId,
            CommandId = command.Id,
            Action = OperatorLogAction.OperationChunk,
            Scope = token.LogScope,
            Count = CommandRules.ChunkSize,
            Reason = reason,
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
                        Filter = OperationEndpoints.ParseFilter(token.Filter),
                        HighWaterMark = token.HighWaterMark,
                        Cursor = body.Cursor,
                        ChunkSize = CommandRules.ChunkSize,
                        Arguments = arguments,
                        Reason = reason,
                        User = context.User,
                    },
                    context.RequestAborted
                )
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            // Checked before any row changes, so, like every refused call, it writes no log entry.
            return _Invalid([OperationEndpoints.ChunkRefused(exception, body.Cursor)]);
        }
        catch
        {
            // Some rows of the chunk may have changed before it failed; the attempt is recorded before the error
            // reaches the host's error handling.
            await CommandEndpoints
                .WriteLogAsync(context, entry with { Failed = CommandRules.ChunkSize })
                .ConfigureAwait(false);
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

    // The calling node shows the detail, so it is the first refusal's message, as a single-error problem's is.
    private static ValidationProblem _Invalid(IReadOnlyList<CommandFieldError> errors) =>
        CommandEndpoints.Invalid(errors, errors[0].Message);
}
