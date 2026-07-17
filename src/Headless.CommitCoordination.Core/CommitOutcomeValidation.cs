// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Checks;

namespace Headless.CommitCoordination;

internal static class CommitOutcomeValidation
{
    internal static void ThrowIfNotTerminal(CommitOutcome outcome)
    {
        Argument.IsGreaterThanOrEqualTo(
            (int)outcome,
            (int)CommitOutcome.Committed,
            "A terminal commit outcome is required.",
            nameof(outcome)
        );
        Argument.IsLessThanOrEqualTo(
            (int)outcome,
            (int)CommitOutcome.RolledBack,
            "A terminal commit outcome is required.",
            nameof(outcome)
        );
    }
}
