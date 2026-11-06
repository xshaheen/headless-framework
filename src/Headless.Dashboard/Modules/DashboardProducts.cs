// Copyright (c) Mahmoud Shaheen. All rights reserved.

namespace Headless.Dashboard;

/// <summary>
/// The products a module may join, in the order the shell shows their tabs. The dashboard fixes the order rather than
/// the host's registration, so a product's tab sits in the same place on every host. The SDK's Vite plugin checks a
/// module's product against the same ids at build time.
/// </summary>
internal static class DashboardProducts
{
    public static IReadOnlyList<DashboardProduct> All { get; } =
    [
        new("jobs", "Jobs"),
        new("messaging", "Messaging"),
        new("cluster", "Cluster"),
        new("platform", "Platform"),
        new("dev-inbox", "Dev inbox"),
        new("diagnostics", "Diagnostics"),
    ];

    /// <summary>Finds the product with the given id, or <see langword="null"/> when no product has it.</summary>
    public static DashboardProduct? Find(string? id)
    {
        return All.FirstOrDefault(product => string.Equals(product.Id, id, StringComparison.Ordinal));
    }
}

/// <summary>One product: its id, as a module's manifest names it, and the label its tab shows.</summary>
internal sealed record DashboardProduct(string Id, string Label);
