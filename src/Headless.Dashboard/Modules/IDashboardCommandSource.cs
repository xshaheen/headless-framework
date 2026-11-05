// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Security.Claims;

namespace Headless.Dashboard;

/// <summary>
/// A module capability: the operator commands the module offers, such as Requeue or Cancel, which the shell draws and
/// serves at <c>POST {base}/api/{module}/commands/{id}</c>.
/// </summary>
/// <remarks>
/// <para>
/// The shell owns every command endpoint. Before it calls <see cref="ExecuteAsync"/> it enforces the command's
/// declared permission, confirmation, reason, argument schema, and <see cref="DashboardCommand.Visibility"/>, writes
/// the operator-log entry, and answers <c>404</c> for a command the host disabled. A command given a filter instead
/// of ids runs as an Operation through the module's <see cref="IDashboardOperationRunner"/>.
/// </para>
/// <para>
/// <see cref="Commands"/> is read once, when the shell builds the dashboard's manifest, so it must be a fixed list
/// built without I/O.
/// </para>
/// <para>
/// Implemented by module packages on their <see cref="IDashboardModule"/>. New members arrive as default interface
/// members; existing members never gain a parameter, and the request records grow instead.
/// </para>
/// </remarks>
[PublicAPI]
public interface IDashboardCommandSource
{
    /// <summary>Gets the commands the module offers.</summary>
    IReadOnlyList<DashboardCommand> Commands { get; }

    /// <summary>
    /// Answers, for each object, which of its kind's commands are allowed now, with a reason for each refused one,
    /// such as <c>Running - cancel instead</c>. The shell disables a control with that reason, so it never offers an
    /// action this module would refuse.
    /// </summary>
    /// <param name="request">The objects and who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends.</param>
    /// <returns>
    /// One answer per object and command. The shell treats a command missing from the answer for an object as refused,
    /// with no reason.
    /// </returns>
    Task<IReadOnlyList<DashboardCommandAvailability>> GetAllowedCommandsAsync(
        DashboardAllowedCommandsRequest request,
        CancellationToken cancellationToken
    );

    /// <summary>Runs an accepted command against the given ids.</summary>
    /// <param name="request">The command, the ids, the validated arguments, the reason, and who is asking.</param>
    /// <param name="cancellationToken">Cancelled when the request ends.</param>
    /// <returns>One outcome per id.</returns>
    Task<DashboardCommandResult> ExecuteAsync(DashboardCommandRequest request, CancellationToken cancellationToken);
}

/// <summary>A command a module declares: what it acts on and the rules the shell enforces before running it.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardCommand
{
    /// <summary>
    /// Gets the command's id, prefixed with the module id, such as <c>jobs.requeue</c>. It is the command's URL
    /// segment and the contribution id the host disables it by.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>Gets the command's label, such as <c>Requeue</c>.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the kind of object the command acts on, such as <c>run</c>.</summary>
    public required string ObjectKind { get; init; }

    /// <summary>Gets the permission the command needs. Defaults to <see cref="DashboardCommandPermission.Operate"/>.</summary>
    public DashboardCommandPermission Permission { get; init; }

    /// <summary>
    /// Gets a named capability the command needs instead of the plain operate permission, such as
    /// <c>jobs.delete</c>, or <see langword="null"/>. Until the host maps a capability to a policy, it needs the module's
    /// operate policy. Ignored when <see cref="Permission"/> is <see cref="DashboardCommandPermission.View"/>.
    /// </summary>
    public string? Capability { get; init; }

    /// <summary>
    /// Gets how the operator confirms the command. Whatever is declared, a confirmed command over more than 100 rows
    /// is confirmed as <see cref="DashboardConfirmation.Typed"/>.
    /// </summary>
    public DashboardConfirmation Confirmation { get; init; }

    /// <summary>Gets a value indicating whether the operator must give a reason, which the operator log records.</summary>
    public bool RequiresReason { get; init; }

    /// <summary>
    /// Gets the id of the command that undoes this one, or <see langword="null"/>. A command with an inverse runs
    /// without confirmation, and the shell offers Undo instead.
    /// </summary>
    public string? InverseCommandId { get; init; }

    /// <summary>
    /// Gets the channels that may call the command. Defaults to both: the shell draws it, and API clients call it.
    /// </summary>
    public DashboardChannels Visibility { get; init; } = DashboardChannels.Ui | DashboardChannels.Api;

    /// <summary>
    /// Gets the command's arguments, which the shell renders as a form and validates on the server; empty when the
    /// command takes none.
    /// </summary>
    public IReadOnlyList<DashboardCommandArgument> Arguments { get; init; } = [];
}

/// <summary>The permission a command needs.</summary>
[PublicAPI]
public enum DashboardCommandPermission
{
    /// <summary>The module's operate policy, or the command's capability when it names one.</summary>
    Operate = 0,

    /// <summary>The module's view policy: the command changes nothing an operator owns.</summary>
    View = 1,
}

/// <summary>How an operator confirms a command.</summary>
[PublicAPI]
public enum DashboardConfirmation
{
    /// <summary>The command runs without a confirm step.</summary>
    None = 0,

    /// <summary>The operator confirms in a dialog.</summary>
    Confirm = 1,

    /// <summary>The operator confirms by typing the number of rows the command touches.</summary>
    Typed = 2,
}

/// <summary>The channels a command call arrives through.</summary>
[Flags]
[PublicAPI]
public enum DashboardChannels
{
    /// <summary>No channel.</summary>
    None = 0,

    /// <summary>The dashboard's own UI.</summary>
    Ui = 1,

    /// <summary>An API client calling the command endpoint directly.</summary>
    Api = 2,
}

/// <summary>One argument of a command: a small JSON-Schema subset the shell validates and renders as a form field.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardCommandArgument
{
    /// <summary>Gets the argument's name, its key in the request.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the argument's form label.</summary>
    public required string Label { get; init; }

    /// <summary>Gets the argument's value kind.</summary>
    public required DashboardArgumentKind Kind { get; init; }

    /// <summary>Gets a value indicating whether the argument must be given.</summary>
    public bool Required { get; init; }

    /// <summary>Gets the only values the argument accepts, or <see langword="null"/> for any value of its kind.</summary>
    public IReadOnlyList<string>? AllowedValues { get; init; }

    /// <summary>Gets the smallest number accepted, or <see langword="null"/>. Applies to number kinds.</summary>
    public double? Minimum { get; init; }

    /// <summary>Gets the largest number accepted, or <see langword="null"/>. Applies to number kinds.</summary>
    public double? Maximum { get; init; }

    /// <summary>Gets the longest text accepted, or <see langword="null"/>. Applies to <see cref="DashboardArgumentKind.Text"/>.</summary>
    public int? MaxLength { get; init; }
}

/// <summary>The value kind of a command argument, after JSON Schema's <c>type</c>.</summary>
[PublicAPI]
public enum DashboardArgumentKind
{
    /// <summary>A string.</summary>
    Text = 0,

    /// <summary>An integer.</summary>
    WholeNumber = 1,

    /// <summary>Any number.</summary>
    Number = 2,

    /// <summary>A boolean.</summary>
    Toggle = 3,
}

/// <summary>A request for the commands allowed now on some objects.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardAllowedCommandsRequest
{
    /// <summary>Gets the objects, all of this module.</summary>
    public required IReadOnlyList<ObjectRef> Targets { get; init; }

    /// <summary>Gets the signed-in user who is asking.</summary>
    public required ClaimsPrincipal User { get; init; }
}

/// <summary>Whether one command is allowed now on one object.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardCommandAvailability
{
    /// <summary>Gets the object.</summary>
    public required ObjectRef Target { get; init; }

    /// <summary>Gets the command's id.</summary>
    public required string CommandId { get; init; }

    /// <summary>Gets a value indicating whether the command is allowed now.</summary>
    public required bool Allowed { get; init; }

    /// <summary>Gets why a refused command is refused, shown on the disabled control, or <see langword="null"/>.</summary>
    public string? DisabledReason { get; init; }
}

/// <summary>An accepted command call, after the shell enforced its rules.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardCommandRequest
{
    /// <summary>Gets the command's id.</summary>
    public required string CommandId { get; init; }

    /// <summary>Gets the ids of the objects to act on, all of the command's object kind.</summary>
    public required IReadOnlyList<string> Ids { get; init; }

    /// <summary>Gets the arguments, validated against the command's schema; empty when it takes none.</summary>
    public IReadOnlyDictionary<string, JsonElement> Arguments { get; init; } =
        new Dictionary<string, JsonElement>(StringComparer.Ordinal);

    /// <summary>Gets the operator's reason, or <see langword="null"/> when none was asked.</summary>
    public string? Reason { get; init; }

    /// <summary>Gets the channel the call claimed.</summary>
    public required DashboardChannels Channel { get; init; }

    /// <summary>Gets the signed-in user who runs the command.</summary>
    public required ClaimsPrincipal User { get; init; }
}

/// <summary>The outcome of a command call: one result per row.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardCommandResult
{
    /// <summary>Gets one result per row the call named.</summary>
    public required IReadOnlyList<DashboardRowResult> Rows { get; init; }
}

/// <summary>What a command did to one row.</summary>
/// <remarks>This record grows by <see langword="init"/> members.</remarks>
[PublicAPI]
public sealed record DashboardRowResult
{
    /// <summary>Gets the row's id.</summary>
    public required string Id { get; init; }

    /// <summary>Gets the outcome.</summary>
    public required DashboardRowOutcome Outcome { get; init; }

    /// <summary>
    /// Gets why the row was skipped or failed, such as <c>no longer failed</c>, or <see langword="null"/>.
    /// </summary>
    public string? Reason { get; init; }
}

/// <summary>What a command did to one row.</summary>
[PublicAPI]
public enum DashboardRowOutcome
{
    /// <summary>The command changed the row.</summary>
    Done = 0,

    /// <summary>The row no longer qualified, so the command left it alone.</summary>
    Skipped = 1,

    /// <summary>The command tried to change the row and failed.</summary>
    Failed = 2,
}
