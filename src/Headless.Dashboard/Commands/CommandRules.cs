// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>The limits and confirmation rules the shell applies to every module's commands alike.</summary>
internal static class CommandRules
{
    /// <summary>
    /// The row count past which a confirmed command is confirmed by typing the count, whatever it declares: a click
    /// is too easy a confirmation for that many rows.
    /// </summary>
    public const int TypedConfirmationAbove = 100;

    /// <summary>
    /// The most rows one command call or one Operation may act on. A larger scope is refused so the operator narrows
    /// the filter, because a mistaken scope that large is hard to undo.
    /// </summary>
    public const int MaxRows = 100_000;

    /// <summary>How many rows the confirm step shows as a sample of the Operation's scope.</summary>
    public const int SampleSize = 5;

    /// <summary>The most rows one Operation chunk acts on.</summary>
    public const int ChunkSize = 500;

    /// <summary>The most characters an operator's reason may hold; it is written to every operator-log entry.</summary>
    public const int MaxReasonLength = 1_000;

    /// <summary>How many objects one allowed-now request may ask about: one list page.</summary>
    public const int MaxAvailabilityTargets = ChunkSize;

    /// <summary>How long an Operation's scope token stays valid after confirm.</summary>
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromHours(1);

    /// <summary>
    /// The confirmation the command declares, before any row count applies: a command with an inverse runs without
    /// one, because the shell offers Undo instead.
    /// </summary>
    public static DashboardConfirmation Declared(DashboardCommand command)
    {
        return command.InverseCommandId is null ? command.Confirmation : DashboardConfirmation.None;
    }

    /// <summary>The confirmation a call over <paramref name="rowCount"/> rows needs.</summary>
    public static DashboardConfirmation Effective(DashboardCommand command, long rowCount)
    {
        var declared = Declared(command);

        return declared != DashboardConfirmation.None && rowCount > TypedConfirmationAbove
            ? DashboardConfirmation.Typed
            : declared;
    }

    /// <summary>
    /// Checks that a call over <paramref name="rowCount"/> rows carries the confirmation it needs: <c>confirmed</c> for
    /// a confirm dialog, and <c>typedCount</c> equal to the row count for a typed one.
    /// </summary>
    /// <returns>The error to answer with, or <see langword="null"/> when the call is confirmed.</returns>
    public static CommandFieldError? CheckConfirmation(
        DashboardCommand command,
        long rowCount,
        bool confirmed,
        long? typedCount
    )
    {
        return Effective(command, rowCount) switch
        {
            DashboardConfirmation.Confirm when !confirmed => new CommandFieldError(
                "confirmed",
                "confirm",
                $"Command '{command.Id}' must be confirmed: send \"confirmed\": true."
            ),
            DashboardConfirmation.Typed when typedCount != rowCount => new CommandFieldError(
                "typedCount",
                "typed",
                $"Command '{command.Id}' over {rowCount.ToString(CultureInfo.InvariantCulture)} rows must be "
                    + "confirmed by typing the row count: send "
                    + $"\"typedCount\": {rowCount.ToString(CultureInfo.InvariantCulture)}."
            ),
            _ => null,
        };
    }
}
