// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Headless.Dashboard;

/// <summary>
/// The shell's command endpoints for one module, under <c>{base}/api/{module}/commands</c>:
/// <list type="bullet">
///   <item><description><c>GET</c>: the module's commands the UI draws, and whether the user may run each.</description></item>
///   <item><description>
///   <c>GET allowed?kind=&amp;id=</c>: which of those commands each object allows now, with the reason for each refused
///   one.
///   </description></item>
///   <item><description>
///   <c>POST {commandId}</c>: runs the command over the ids in the body. A filter runs as an Operation through
///   <see cref="OperationEndpoints"/> instead.
///   </description></item>
/// </list>
/// </summary>
/// <remarks>
/// <para>
/// The shell, not the module, enforces every rule here, so every module's commands behave alike: a command the host
/// disabled, or one the call's channel may not use, answers <c>404</c>; the command's permission answers <c>403</c>; the
/// body, the reason, the argument schema, and the confirmation answer <c>400</c> naming the field and the rule. Only
/// a call that passes all of them reaches the module and writes an operator-log entry, unless the module refuses the
/// whole call with <see cref="DashboardCommandResult.Refusal"/>, which answers <c>409</c> and writes none.
/// </para>
/// <para>
/// These routes sit inside the module's own API prefix, <c>{base}/api/{module}</c>, so the <c>commands</c> segment
/// there belongs to the shell: a module that maps its own endpoint under <c>commands/</c> makes the route ambiguous.
/// </para>
/// </remarks>
internal static class CommandEndpoints
{
    /// <summary>
    /// The header the dashboard's UI sends with <c>ui</c>; a call without it is an API call. The channel is claimed,
    /// not proven, so it decides only which commands a caller is offered and what the operator log records.
    /// </summary>
    public const string ChannelHeader = "X-Headless-Dashboard-Channel";

    // The shell's SPA reads these field names and enum values; the host's own JSON options must not change them.
    internal static readonly JsonSerializerOptions JsonOptions = _CreateJsonOptions();

    /// <summary>Maps the command endpoints of <paramref name="module"/> on <paramref name="dashboard"/>.</summary>
    internal static void Map(RouteGroupBuilder dashboard, MergedModule module)
    {
        var commands = dashboard
            .MapGroup($"/api/{module.Id}/commands")
            .RequireDashboardShellAccess(module.Id)
            .RequireDashboardRequestIntegrity();
        commands.ExcludeFromDescription();

        commands.MapGet(string.Empty, Handler(module, _ListAsync));
        commands.MapGet("/allowed", Handler(module, _GetAllowedAsync));
#pragma warning disable ASP0018 // False positive: the handler reads commandId from the route values, not a bound parameter.
        commands.MapPost("/{commandId}", Handler(module, _RunAsync));
#pragma warning restore ASP0018

        OperationEndpoints.Map(commands, module);
    }

    // A lambda returning Task<IResult> binds to the RequestDelegate overload, which discards the result.
    internal static Func<HttpContext, Task<IResult>> Handler(
        MergedModule module,
        Func<HttpContext, MergedModule, CancellationToken, Task<IResult>> handler
    )
    {
        return context => handler(context, module, context.RequestAborted);
    }

    /// <summary>
    /// Finds the command the route names and checks that the call may use it: the host has not disabled it, the call's
    /// channel may call it, and the user has its permission.
    /// </summary>
    /// <returns>The command and the call's channel, or the result to answer with.</returns>
    internal static async Task<(DashboardCommand? Command, DashboardChannels Channel, IResult? Refused)> ResolveAsync(
        HttpContext context,
        MergedModule module
    )
    {
        var id = context.Request.RouteValues["commandId"] as string;

        // The merged manifest has already dropped the commands the host disabled.
        var command = module.ServerCommands.FirstOrDefault(c => string.Equals(c.Id, id, StringComparison.Ordinal));
        var channel = GetChannel(context);

        if (command is null || (command.Visibility & channel) == DashboardChannels.None)
        {
            return (null, channel, TypedResults.NotFound());
        }

        var refusal = await CommandAuthorization.GetRefusalAsync(context, module.Id, command).ConfigureAwait(false);

        return refusal is null
            ? (command, channel, null)
            : (null, channel, TypedResults.Problem(detail: refusal, statusCode: StatusCodes.Status403Forbidden));
    }

    /// <summary>Checks the reason and the arguments of a call to <paramref name="command"/>.</summary>
    /// <returns>The validated arguments, and every error found.</returns>
    internal static (IReadOnlyDictionary<string, JsonElement> Arguments, List<CommandFieldError> Errors) Validate(
        HttpContext context,
        DashboardCommand command,
        CommandCallBody body
    )
    {
        var errors = new List<CommandFieldError>();

        if (command.RequiresReason && string.IsNullOrWhiteSpace(body.Reason))
        {
            errors.Add(new CommandFieldError("reason", "required", $"Command '{command.Id}' requires a reason."));
        }
        else if (body.Reason is { Length: > CommandRules.MaxReasonLength })
        {
            errors.Add(
                new CommandFieldError(
                    "reason",
                    "maxLength",
                    $"The reason must be at most {CommandRules.MaxReasonLength} characters."
                )
            );
        }

        var arguments = ArgumentSchemaValidator.Validate(command.Arguments, body.Arguments, Now(context));
        errors.AddRange(arguments.Errors);

        return (arguments.Values, errors);
    }

    /// <summary>The reason to pass on: <see langword="null"/> for a blank one.</summary>
    internal static string? Reason(CommandCallBody body)
    {
        return string.IsNullOrWhiteSpace(body.Reason) ? null : body.Reason;
    }

    /// <summary>Answers <c>400</c> naming each refused field and the rule it broke.</summary>
    internal static ValidationProblem Invalid(IReadOnlyCollection<CommandFieldError> errors, string? detail = null)
    {
        var messages = errors
            .GroupBy(static error => error.Field, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static error => error.Message).ToArray(),
                StringComparer.Ordinal
            );
        var rules = errors
            .GroupBy(static error => error.Field, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.First().Rule, StringComparer.Ordinal);

        return TypedResults.ValidationProblem(
            messages,
            detail: detail,
            extensions: new Dictionary<string, object?>(StringComparer.Ordinal) { ["rules"] = rules }
        );
    }

    internal static ValidationProblem Invalid(CommandFieldError error) => Invalid([error], error.Message);

    /// <summary>Counts the outcomes of <paramref name="rows"/>.</summary>
    internal static (long Done, long Skipped, long Failed) Tally(IReadOnlyList<DashboardRowResult> rows)
    {
        long done = 0,
            skipped = 0,
            failed = 0;

        foreach (var row in rows)
        {
            switch (row.Outcome)
            {
                case DashboardRowOutcome.Done:
                    done++;
                    break;
                case DashboardRowOutcome.Skipped:
                    skipped++;
                    break;
                default:
                    failed++;
                    break;
            }
        }

        return (done, skipped, failed);
    }

    internal static DashboardChannels GetChannel(HttpContext context)
    {
        return string.Equals(
            context.Request.Headers[ChannelHeader].ToString(),
            "ui",
            StringComparison.OrdinalIgnoreCase
        )
            ? DashboardChannels.Ui
            : DashboardChannels.Api;
    }

    internal static Task WriteLogAsync(HttpContext context, OperatorLogEntry entry)
    {
        return context.RequestServices.GetRequiredService<OperatorLogWriter>().WriteAsync(entry);
    }

    internal static DateTimeOffset Now(HttpContext context)
    {
        return context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow();
    }

    private static async Task<IResult> _ListAsync(
        HttpContext context,
        MergedModule module,
        CancellationToken cancellationToken
    )
    {
        var supportsFilter = module.Module is IDashboardOperationRunner;
        var definitions = new List<CommandDefinition>(module.ServerCommands.Count);

        // Only the commands the UI may call are drawn; an API-only command is called by scripts that already know it.
        foreach (var command in module.ServerCommands.Where(static c => c.Visibility.HasFlag(DashboardChannels.Ui)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var refusal = await CommandAuthorization.GetRefusalAsync(context, module.Id, command).ConfigureAwait(false);
            definitions.Add(CommandDefinition.Create(command, supportsFilter, refusal));
        }

        return Results.Json(new CommandListResponse(definitions), JsonOptions);
    }

    private static async Task<IResult> _GetAllowedAsync(
        HttpContext context,
        MergedModule module,
        CancellationToken cancellationToken
    )
    {
        var query = context.Request.Query;
        var kind = query["kind"].ToString();
        string[] ids =
        [
            .. query["id"]
                .Where(static id => !string.IsNullOrWhiteSpace(id))
                .Select(static id => id!)
                .Distinct(StringComparer.Ordinal),
        ];

        if (kind.Length == 0 || ids.Length == 0)
        {
            return Invalid(new CommandFieldError("id", "required", "Name the objects with kind= and one id= each."));
        }

        if (ids.Length > CommandRules.MaxAvailabilityTargets)
        {
            return Invalid(
                new CommandFieldError(
                    "id",
                    "maxItems",
                    $"Ask about at most {CommandRules.MaxAvailabilityTargets} objects at once."
                )
            );
        }

        var commands = module
            .ServerCommands.Where(c =>
                c.Visibility.HasFlag(DashboardChannels.Ui)
                && string.Equals(c.ObjectKind, kind, StringComparison.Ordinal)
            )
            .ToArray();
        var refusals = new Dictionary<string, string?>(commands.Length, StringComparer.Ordinal);

        foreach (var command in commands)
        {
            refusals[command.Id] = await CommandAuthorization
                .GetRefusalAsync(context, module.Id, command)
                .ConfigureAwait(false);
        }

        var answers = await _AskModuleAsync(context, module, kind, ids, commands.Length, cancellationToken)
            .ConfigureAwait(false);

        var objects = ids.Select(id => new ObjectAvailabilityResponse(
                kind,
                id,
                [
                    .. commands.Select(command =>
                    {
                        // The user's permission comes first: an action they may not take is never offered as allowed.
                        if (refusals[command.Id] is { } refusal)
                        {
                            return new CommandAvailabilityResponse(command.Id, Allowed: false, refusal);
                        }

                        // A command missing from the module's answer is refused, with no reason to show.
                        return answers.TryGetValue((id, command.Id), out var answer)
                            ? new CommandAvailabilityResponse(
                                command.Id,
                                answer.Allowed,
                                answer.Allowed ? null : answer.DisabledReason
                            )
                            : new CommandAvailabilityResponse(command.Id, Allowed: false, DisabledReason: null);
                    }),
                ]
            ))
            .ToArray();

        return Results.Json(new AvailabilityResponse(objects), JsonOptions);
    }

    private static async Task<Dictionary<(string Id, string CommandId), DashboardCommandAvailability>> _AskModuleAsync(
        HttpContext context,
        MergedModule module,
        string kind,
        string[] ids,
        int commandCount,
        CancellationToken cancellationToken
    )
    {
        var answers = new Dictionary<(string, string), DashboardCommandAvailability>();

        if (commandCount == 0)
        {
            return answers;
        }

        var source = (IDashboardCommandSource)module.Module;
        var request = new DashboardAllowedCommandsRequest
        {
            Targets =
            [
                .. ids.Select(id => new ObjectRef
                {
                    Module = module.Id,
                    Kind = kind,
                    Id = id,
                }),
            ],
            User = context.User,
        };

        var availability = await source.GetAllowedCommandsAsync(request, cancellationToken).ConfigureAwait(false);

        foreach (var answer in availability)
        {
            if (
                string.Equals(answer.Target.Module, module.Id, StringComparison.Ordinal)
                && string.Equals(answer.Target.Kind, kind, StringComparison.Ordinal)
            )
            {
                answers[(answer.Target.Id, answer.CommandId)] = answer;
            }
        }

        return answers;
    }

    private static async Task<IResult> _RunAsync(
        HttpContext context,
        MergedModule module,
        CancellationToken cancellationToken
    )
    {
        var (command, channel, refused) = await ResolveAsync(context, module).ConfigureAwait(false);

        if (refused is not null)
        {
            return refused;
        }

        var (body, bodyErrors) = await CommandCallBody
            .ReadAsync(context.Request, cancellationToken)
            .ConfigureAwait(false);

        if (body is null)
        {
            return Invalid(bodyErrors);
        }

        if (body.Filter is not null)
        {
            return Invalid(
                new CommandFieldError(
                    "filter",
                    "operation",
                    $"A command over a filter runs as an Operation: confirm it first at commands/{command!.Id}/confirm."
                )
            );
        }

        if (body.Ids is not { Count: > 0 } ids)
        {
            return Invalid(new CommandFieldError("ids", "required", "Name the rows to act on in 'ids'."));
        }

        if (ids.Count > CommandRules.MaxRows)
        {
            return Invalid(
                new CommandFieldError(
                    "ids",
                    "maxItems",
                    $"A command acts on at most {CommandRules.MaxRows} rows. Narrow the selection."
                )
            );
        }

        var (arguments, errors) = Validate(context, command!, body);

        if (CommandRules.CheckConfirmation(command!, ids.Count, body.Confirmed, body.TypedCount) is { } unconfirmed)
        {
            errors.Add(unconfirmed);
        }

        if (errors.Count > 0)
        {
            return Invalid(errors);
        }

        var request = new DashboardCommandRequest
        {
            CommandId = command!.Id,
            Ids = ids,
            Arguments = arguments,
            Reason = Reason(body),
            Channel = channel,
            User = context.User,
        };
        var entry = new OperatorLogEntry
        {
            Timestamp = Now(context),
            Actor = CommandAuthorization.GetActor(context.User),
            Channel = channel,
            Module = module.Id,
            CommandId = command.Id,
            Action = OperatorLogAction.Run,
            Ids = ids,
            Count = ids.Count,
            Reason = request.Reason,
        };

        DashboardCommandResult result;

        try
        {
            result = await ((IDashboardCommandSource)module.Module)
                .ExecuteAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            // The module may have changed some rows before it failed; the attempt is recorded before the error
            // propagates to the host's error handling.
            await WriteLogAsync(context, entry with { Failed = ids.Count }).ConfigureAwait(false);
            throw;
        }

        if (result.Refusal is { } refusal)
        {
            return TypedResults.Problem(detail: refusal, statusCode: StatusCodes.Status409Conflict);
        }

        var (done, skipped, failed) = Tally(result.Rows);
        await WriteLogAsync(
                context,
                entry with
                {
                    Timestamp = Now(context),
                    Done = done,
                    Skipped = skipped,
                    Failed = failed,
                }
            )
            .ConfigureAwait(false);

        return Results.Json(new CommandRowsResponse(result.Rows), JsonOptions);
    }

    private static JsonSerializerOptions _CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));

        return options;
    }
}

internal sealed record CommandListResponse(IReadOnlyList<CommandDefinition> Commands);

internal sealed record AvailabilityResponse(IReadOnlyList<ObjectAvailabilityResponse> Objects);

internal sealed record ObjectAvailabilityResponse(
    string Kind,
    string Id,
    IReadOnlyList<CommandAvailabilityResponse> Commands
);

internal sealed record CommandAvailabilityResponse(string Id, bool Allowed, string? DisabledReason);

internal sealed record CommandRowsResponse(IReadOnlyList<DashboardRowResult> Rows);
