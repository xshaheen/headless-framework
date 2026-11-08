// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Dashboard;

/// <summary>
/// The endpoints that run a command over a filter as an Operation the browser drives chunk by chunk, for a module that
/// implements <see cref="IDashboardOperationRunner"/>:
/// <list type="bullet">
///   <item><description>
///   <c>POST {base}/api/{module}/commands/{commandId}/confirm</c>: freezes the scope and answers its exact count, a
///   sample, the confirmation the count needs, and a signed scope token. A scope over
///   <see cref="CommandRules.MaxRows"/> rows is refused, so the operator narrows the filter.
///   </description></item>
///   <item><description>
///   <c>POST {base}/api/{module}/commands/{commandId}/chunks</c>: runs the next chunk of the token's scope after the
///   given cursor and answers its per-row outcomes and the next cursor.
///   </description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// Nothing about an Operation is stored: the token carries its scope, and the cursor its progress, so any node that
/// shares the Data Protection key ring can serve any chunk, and Stop is the browser not asking for the next one. The
/// module's conditional update keeps a chunk from touching a row twice, across nodes and across runs.
/// </para>
/// <para>
/// Each chunk checks everything again: the command is still enabled, the user still has its permission (its named
/// capability, or operate), the token is intact, unexpired, issued by this user, and for this module and command, and
/// the call carries the confirmation its count needs.
/// </para>
/// </remarks>
internal static class OperationEndpoints
{
    /// <summary>Maps the Operation endpoints of <paramref name="module"/> on its command group.</summary>
    internal static void Map(RouteGroupBuilder commands, MergedModule module)
    {
        if (module.Module is not IDashboardOperationRunner)
        {
            return;
        }

#pragma warning disable ASP0018 // False positive: the handlers read commandId from the route values, not a bound parameter.
        commands.MapPost("/{commandId}/confirm", CommandEndpoints.Handler(module, _ConfirmAsync));
        commands.MapPost("/{commandId}/chunks", CommandEndpoints.Handler(module, _RunChunkAsync));
#pragma warning restore ASP0018
    }

    private static async Task<IResult> _ConfirmAsync(
        HttpContext context,
        MergedModule module,
        CancellationToken cancellationToken
    )
    {
        var (command, channel, refused) = await CommandEndpoints.ResolveAsync(context, module).ConfigureAwait(false);

        if (refused is not null)
        {
            return refused;
        }

        var (body, bodyErrors) = await CommandCallBody
            .ReadAsync(context.Request, cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            return CommandEndpoints.Invalid(bodyErrors);
        }

        var (arguments, errors) = CommandEndpoints.Validate(command!, body);

        // An Operation changes many rows at once, so it always records why, even for a command one row runs without one.
        if (!command!.RequiresReason && string.IsNullOrWhiteSpace(body.Reason))
        {
            errors.Insert(0, new CommandFieldError("reason", "required", "An Operation requires a reason."));
        }

        if (body.Filter is null)
        {
            errors.Insert(0, new CommandFieldError("filter", "required", "Send the filter the Operation runs over."));
        }

        if (errors.Count > 0)
        {
            return CommandEndpoints.Invalid(errors);
        }

        var filter = body.Filter!.Value;
        DashboardOperationScope scope;

        try
        {
            scope = await ((IDashboardOperationRunner)module.Module)
                .PrepareAsync(
                    new DashboardOperationScopeRequest
                    {
                        CommandId = command!.Id,
                        Filter = filter,
                        CountLimit = CommandRules.MaxRows,
                        SampleSize = CommandRules.SampleSize,
                        User = context.User,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            // The runner refuses a filter it does not accept, such as an unknown store, before it reads anything.
            return CommandEndpoints.Invalid(new CommandFieldError("filter", "invalid", _Message(exception)));
        }

        if (scope.Count > CommandRules.MaxRows)
        {
            return CommandEndpoints.Invalid(
                new CommandFieldError(
                    "filter",
                    "maxCount",
                    $"The filter matches more than {CommandRules.MaxRows.ToString("N0", CultureInfo.InvariantCulture)} "
                        + "rows. Narrow the filter and confirm again."
                )
            );
        }

        var now = CommandEndpoints.Now(context);
        var token = new OperationScopeToken
        {
            OperationId = Guid.CreateVersion7(now).ToString("N"),
            Module = module.Id,
            CommandId = command.Id,
            Filter = filter.GetRawText(),
            HighWaterMark = scope.HighWaterMark,
            Count = scope.Count,
            Actor = CommandAuthorization.GetActor(context.User),
            Reason = CommandEndpoints.Reason(body),
            Arguments =
                arguments.Count == 0
                    ? null
                    : JsonSerializer.Serialize(
                        arguments.ToDictionary(StringComparer.Ordinal),
                        OperationScopeTokenJsonContext.Default.DictionaryStringJsonElement
                    ),
            ExpiresAt = now + CommandRules.TokenLifetime,
        };
        var protector = context.RequestServices.GetRequiredService<OperationScopeTokenProtector>();
        var signed = protector.Protect(token);

        await CommandEndpoints
            .WriteLogAsync(
                context,
                new OperatorLogEntry
                {
                    Timestamp = now,
                    Actor = token.Actor,
                    Channel = channel,
                    Module = module.Id,
                    CommandId = command.Id,
                    Action = OperatorLogAction.OperationConfirmed,
                    Scope = token.Filter,
                    Count = scope.Count,
                    Reason = token.Reason,
                    OperationId = token.OperationId,
                }
            )
            .ConfigureAwait(false);

        return Results.Json(
            new OperationConfirmResponse(
                token.OperationId,
                scope.Count,
                CommandRules.Effective(command, scope.Count),
                [.. scope.Sample.Take(CommandRules.SampleSize)],
                signed,
                token.ExpiresAt,
                CommandRules.ChunkSize
            ),
            CommandEndpoints.JsonOptions
        );
    }

    private static async Task<IResult> _RunChunkAsync(
        HttpContext context,
        MergedModule module,
        CancellationToken cancellationToken
    )
    {
        // The command's permission is evaluated again on every chunk, so revoking it stops the Operation at the next one.
        var (command, channel, refused) = await CommandEndpoints.ResolveAsync(context, module).ConfigureAwait(false);

        if (refused is not null)
        {
            return refused;
        }

        var (body, bodyErrors) = await CommandCallBody
            .ReadAsync(context.Request, cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            return CommandEndpoints.Invalid(bodyErrors);
        }

        if (string.IsNullOrEmpty(body.Token))
        {
            return CommandEndpoints.Invalid(
                new CommandFieldError("token", "required", "Send the token the confirm step returned.")
            );
        }

        var protector = context.RequestServices.GetRequiredService<OperationScopeTokenProtector>();

        if (!protector.TryUnprotect(body.Token, out var token, out var tokenError))
        {
            return CommandEndpoints.Invalid(tokenError);
        }

        if (
            !string.Equals(token.Module, module.Id, StringComparison.Ordinal)
            || !string.Equals(token.CommandId, command!.Id, StringComparison.Ordinal)
        )
        {
            return CommandEndpoints.Invalid(
                new CommandFieldError(
                    "token",
                    "scope",
                    $"The token was confirmed for command '{token.CommandId}' of module '{token.Module}'. Send it to "
                        + "that command's chunk endpoint."
                )
            );
        }

        var actor = CommandAuthorization.GetActor(context.User);

        if (!string.Equals(token.Actor, actor, StringComparison.Ordinal))
        {
            return TypedResults.Problem(
                detail: "This Operation was confirmed by another operator. Confirm it again to run it yourself.",
                statusCode: StatusCodes.Status403Forbidden
            );
        }

        if (CommandRules.CheckConfirmation(command, token.Count, body.Confirmed, body.TypedCount) is { } unconfirmed)
        {
            return CommandEndpoints.Invalid(unconfirmed);
        }

        var entry = new OperatorLogEntry
        {
            Timestamp = CommandEndpoints.Now(context),
            Actor = actor,
            Channel = channel,
            Module = module.Id,
            CommandId = command.Id,
            Action = OperatorLogAction.OperationChunk,
            Scope = token.Filter,
            Count = CommandRules.ChunkSize,
            Reason = token.Reason,
            OperationId = token.OperationId,
            Cursor = body.Cursor,
        };

        DashboardOperationChunk chunk;

        try
        {
            chunk = await ((IDashboardOperationRunner)module.Module)
                .RunChunkAsync(
                    new DashboardOperationChunkRequest
                    {
                        CommandId = command.Id,
                        Filter = _ParseElement(token.Filter),
                        HighWaterMark = token.HighWaterMark,
                        Cursor = body.Cursor,
                        ChunkSize = CommandRules.ChunkSize,
                        Arguments = _ParseArguments(token.Arguments),
                        Reason = token.Reason,
                        User = context.User,
                    },
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            // The runner checks the scope and the cursor before it changes a row, so a refused one changed nothing and,
            // like every other refused call, writes no log entry.
            return CommandEndpoints.Invalid(
                new CommandFieldError(body.Cursor is null ? "token" : "cursor", "invalid", _Message(exception))
            );
        }
        catch
        {
            // The module may have changed some rows of the chunk before it failed; the attempt is recorded before the
            // error propagates to the host's error handling.
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

        return Results.Json(
            new OperationChunkResponse(token.OperationId, chunk.Rows, chunk.NextCursor, chunk.NextCursor is null),
            CommandEndpoints.JsonOptions
        );
    }

    /// <summary>
    /// The refusal without the <c> (Parameter 'request')</c> suffix .NET adds for a developer: the operator who reads it
    /// never named that parameter.
    /// </summary>
    private static string _Message(ArgumentException exception)
    {
        var suffix = $" (Parameter '{exception.ParamName}')";

        return exception.ParamName is not null && exception.Message.EndsWith(suffix, StringComparison.Ordinal)
            ? exception.Message[..^suffix.Length]
            : exception.Message;
    }

    private static JsonElement _ParseElement(string json)
    {
        return JsonSerializer.Deserialize(json, OperationScopeTokenJsonContext.Default.JsonElement);
    }

    private static Dictionary<string, JsonElement> _ParseArguments(string? json)
    {
        if (json is null)
        {
            return new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        }

        var arguments = JsonSerializer.Deserialize(
            json,
            OperationScopeTokenJsonContext.Default.DictionaryStringJsonElement
        );

        return arguments is null
            ? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
            : new Dictionary<string, JsonElement>(arguments, StringComparer.Ordinal);
    }
}

/// <summary>An Operation's frozen scope, as the confirm dialog shows it.</summary>
/// <param name="OperationId">The id every operator-log entry of the Operation carries.</param>
/// <param name="Count">The exact number of rows in scope.</param>
/// <param name="Confirmation">The confirmation the count needs; <c>typed</c> means typing <paramref name="Count"/>.</param>
/// <param name="Sample">A few rows in scope.</param>
/// <param name="Token">The signed scope every chunk call sends back.</param>
/// <param name="ExpiresAt">When the token stops being accepted.</param>
/// <param name="ChunkSize">The most rows one chunk acts on, for the progress display.</param>
internal sealed record OperationConfirmResponse(
    string OperationId,
    long Count,
    DashboardConfirmation Confirmation,
    IReadOnlyList<DashboardObject> Sample,
    string Token,
    DateTimeOffset ExpiresAt,
    int ChunkSize
);

/// <summary>One chunk's outcome.</summary>
/// <param name="OperationId">The Operation's id.</param>
/// <param name="Rows">One result per row the chunk acted on or skipped.</param>
/// <param name="NextCursor">The cursor the next chunk sends; <see langword="null"/> when the scope is done.</param>
/// <param name="Done">Whether no row is left in scope.</param>
internal sealed record OperationChunkResponse(
    string OperationId,
    IReadOnlyList<DashboardRowResult> Rows,
    string? NextCursor,
    bool Done
);
