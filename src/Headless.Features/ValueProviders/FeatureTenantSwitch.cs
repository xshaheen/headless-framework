// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Features;

/// <summary>
/// Remembers the tenant the feature manager switched to for an operation on another tenant's features, so the
/// lower providers it then reads know the current principal does not describe that tenant.
/// </summary>
internal static class FeatureTenantSwitch
{
    private static readonly AsyncLocal<string?> _SwitchedTo = new();

    /// <summary>The tenant the current scope switched to, or <see langword="null"/> outside such a scope.</summary>
    public static string? SwitchedTo => _SwitchedTo.Value;

    /// <summary>
    /// Marks the current flow as acting for <paramref name="tenantId"/> and returns the previous mark, which the caller
    /// hands back to <see cref="Restore"/> when the operation ends.
    /// </summary>
    public static string? Enter(string tenantId)
    {
        var previous = _SwitchedTo.Value;
        _SwitchedTo.Value = tenantId;

        return previous;
    }

    /// <summary>Restores the mark <see cref="Enter"/> replaced.</summary>
    public static void Restore(string? previous)
    {
        _SwitchedTo.Value = previous;
    }
}
