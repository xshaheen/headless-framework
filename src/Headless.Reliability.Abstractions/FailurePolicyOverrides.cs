// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Reliability;

/// <summary>
/// Numeric values that replace the matching fields of an already resolved <see cref="FailurePolicyDefinition"/>,
/// typically bound from configuration so an operator can tune retry counts and delays without a code change.
/// </summary>
/// <remarks>
/// A <see langword="null"/> property keeps the resolved value. Fail rules are code, so they cannot be overridden here
/// and always come from the resolved policy. Apply the overrides with <see cref="FailurePolicyDefinition.With"/>,
/// which validates the combined result the same way the builder does. The properties are settable so configuration
/// binding can populate them; the definition copies the values, so later changes to this object do not affect it.
/// </remarks>
[PublicAPI]
public sealed class FailurePolicyOverrides
{
    /// <summary>Replaces <see cref="FailurePolicyDefinition.ImmediateRetries"/> when set.</summary>
    public int? ImmediateRetries { get; set; }

    /// <summary>Replaces <see cref="FailurePolicyDefinition.DelayedRetries"/> when set.</summary>
    public int? DelayedRetries { get; set; }

    /// <summary>Replaces <see cref="FailurePolicyDefinition.DelayedInitialDelay"/> when set.</summary>
    public TimeSpan? DelayedInitialDelay { get; set; }

    /// <summary>Replaces <see cref="FailurePolicyDefinition.DelayedMaxDelay"/> when set.</summary>
    public TimeSpan? DelayedMaxDelay { get; set; }
}
