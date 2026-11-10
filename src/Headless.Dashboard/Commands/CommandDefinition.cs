// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// A server command as the shell's UI reads it from <c>GET {base}/api/{module}/commands</c>: what it acts on, the rules
/// it runs under, its argument form, and whether the signed-in user may run it.
/// </summary>
internal sealed record CommandDefinition
{
    public required string Id { get; init; }

    public required string Label { get; init; }

    public required string ObjectKind { get; init; }

    /// <summary>The permission the command needs: <c>view</c> or <c>operate</c>.</summary>
    public required DashboardCommandPermission Permission { get; init; }

    public string? Capability { get; init; }

    /// <summary>
    /// The confirmation the command declares; a confirmed call over <see cref="TypedConfirmationAbove"/> rows is
    /// confirmed as <c>typed</c> instead.
    /// </summary>
    public required DashboardConfirmation Confirmation { get; init; }

    public required int TypedConfirmationAbove { get; init; }

    public required bool RequiresReason { get; init; }

    /// <summary>The command that undoes this one, which the UI offers as Undo; such a command needs no confirmation.</summary>
    public string? InverseCommandId { get; init; }

    /// <summary>Whether the command can run over a filter as an Operation, through its confirm and chunk endpoints.</summary>
    public required bool SupportsFilter { get; init; }

    public required ArgumentSchema Arguments { get; init; }

    /// <summary>Whether the signed-in user may run the command.</summary>
    public required bool CanRun { get; init; }

    /// <summary>Why the user may not run it, such as <c>Needs operate permission</c>; <see langword="null"/> when they may.</summary>
    public string? DisabledReason { get; init; }

    /// <summary>Describes <paramref name="command"/> for a user whose permission check answered <paramref name="refusal"/>.</summary>
    public static CommandDefinition Create(DashboardCommand command, bool supportsFilter, string? refusal)
    {
        return new CommandDefinition
        {
            Id = command.Id,
            Label = command.Label,
            ObjectKind = command.ObjectKind,
            Permission = command.Permission,
            Capability = command.Permission == DashboardCommandPermission.Operate ? command.Capability : null,
            Confirmation = CommandRules.Declared(command),
            TypedConfirmationAbove = CommandRules.TypedConfirmationAbove,
            RequiresReason = command.RequiresReason,
            InverseCommandId = command.InverseCommandId,
            SupportsFilter = supportsFilter,
            Arguments = ArgumentSchema.Create(command.Arguments),
            CanRun = refusal is null,
            DisabledReason = refusal,
        };
    }
}
